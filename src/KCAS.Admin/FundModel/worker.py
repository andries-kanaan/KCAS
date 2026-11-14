"""KCAS research worker. JSON stdin/stdout; progress on stderr. No network or database access."""
import calendar
import json
import math
import sys
import numpy as np
from scipy.optimize import minimize

VERSION = 'markowitz-1'

def months(start, end):
    y, m = map(int, start.split('-'))
    result = []
    while f'{y:04d}-{m:02d}' <= end:
        result.append(f'{y:04d}-{m:02d}')
        m += 1
        if m == 13:
            y, m = y + 1, 1
    return result

def prior_month(month):
    y, m = map(int, month.split('-'))
    return f'{y - (m == 1):04d}-{12 if m == 1 else m - 1:02d}'

def validate(data):
    s = data['scenario']
    for key in ['risk', 'cap', 'cashMin', 'cashMax', 'covShrink', 'meanShrink', 'cost']:
        if not math.isfinite(s[key]) or not 0 <= s[key] <= 1:
            raise ValueError(f'Invalid {key}')
    if s['cashMin'] > s['cashMax'] or s['window'] < s['minimumHistory'] or s['minimumHistory'] < 12:
        raise ValueError('Invalid cash limits or history length')
    if not 12 <= s['window'] <= 240 or s['rebalanceMonths'] not in [1, 3, 6, 12]:
        raise ValueError('Invalid window or rebalance interval')
    if s['basis'] not in ['Price', 'TotalReturn'] or s['mode'] not in ['Backtest', 'Optimise']:
        raise ValueError('Unsupported calculation mode')
    for key in ['start', 'end']:
        import datetime
        datetime.datetime.strptime(s[key], '%Y-%m')
    if s['start'] > s['end'] or len(months(s['start'], s['end'])) > 600:
        raise ValueError('Invalid test period')
    if not -0.99 < s['cashAnnual'] <= 1:
        raise ValueError('Invalid annual cash return')
    keys = [h['security'] for h in data['holdings']]
    if len(keys) != len(set(keys)) or not 1 <= len(keys) <= 150:
        raise ValueError('Select 1-150 distinct holdings')
    for h in data['holdings']:
        if h['value'] < 0 or not 0 <= h['minimum'] <= h['maximum'] <= 1:
            raise ValueError('Invalid holding value or bounds')
    if s.get('feeEnabled'):
        raise ValueError('Fee overlay not available without a validated mandate convention')

def solve(mu, cov, bounds, reference, risk, cost):
    n = len(mu)
    x = np.zeros(n)
    x[-1] = 1
    z = np.r_[x, abs(x-reference)]
    constraints = [
        {'type': 'eq', 'fun': lambda z: z[:n].sum()-1,
         'jac': lambda z: np.r_[np.ones(n), np.zeros(n)]},
        {'type': 'ineq', 'fun': lambda z: risk*risk-z[:n]@cov@z[:n],
         'jac': lambda z: np.r_[-2*cov@z[:n], np.zeros(n)]},
        {'type': 'ineq', 'fun': lambda z: z[n:]-(z[:n]-reference)},
        {'type': 'ineq', 'fun': lambda z: z[n:]+(z[:n]-reference)}]
    out = minimize(lambda z: -mu@z[:n]+cost*z[n:].sum(), z,
                   jac=lambda z: np.r_[-mu, np.full(n, cost)], bounds=bounds+[(0, 2)]*n,
                   constraints=constraints, method='SLSQP', options={'ftol': 1e-12, 'maxiter': 2000})
    w = out.x[:n]
    if (not out.success or abs(w.sum()-1) > 1e-7 or w@cov@w > risk*risk+1e-7
            or any(w[i] < lo-1e-7 or w[i] > hi+1e-7 for i, (lo, hi) in enumerate(bounds))):
        raise ValueError('No verified feasible allocation: '+out.message)
    return w

def allowance(target, previous, cost):
    fee = 0.
    for _ in range(100):
        new = cost*abs(target*(1-fee)-previous).sum()
        if abs(new-fee) < 1e-13:
            return float(new)
        fee = new
    raise ValueError('Trading allowance did not converge')

def calculate(data):
    validate(data)
    s, holdings = data['scenario'], data['holdings']
    keys = [h['security'] for h in holdings]+['CASH']
    n = len(keys)
    cash = (1+s['cashAnnual'])**(1/12)-1
    series = {}
    for security in data['securities']:
        if security['currency'] != data['currency']:
            raise ValueError(f"{security['key']}: import an evidenced {data['currency']} series or FX conversion first")
        observations = {}
        for p in security['series']:
            value = p['price'] if s['basis'] == 'Price' else p.get('totalReturnIndex')
            if value is None:
                continue
            if p['month'] in observations or not math.isfinite(value) or value <= 0:
                raise ValueError('Duplicate month or invalid price: '+security['key'])
            observations[p['month']] = value
        series[security['key']] = {m: v/observations[prior_month(m)]-1 for m, v in observations.items()
                                   if prior_month(m) in observations}

    def train(start):
        end = prior_month(start)
        y, m = map(int, end.split('-'))
        earlier = f'{y-21:04d}-{m:02d}'
        prior = months(earlier, end)[-s['window']:]
        windows, excluded = [], []
        allowed = sorted(set([s['window']]+[k for k in [60, 48, 36, s['minimumHistory']]
                                                       if s['minimumHistory'] <= k <= s['window']]), reverse=True)
        for h in holdings:
            eligible = h['included'] and (s['universe'] != 'Dated' or
                       ((not h.get('from') or h['from'][:7] <= start) and (not h.get('to') or h['to'][:7] >= start)))
            count = 0
            for month in reversed(prior):
                if month not in series.get(h['security'], {}):
                    break
                count += 1
            window = next((k for k in allowed if count >= k), 0) if eligible else 0
            windows.append(window)
            if not window:
                if h['minimum'] > 0:
                    raise ValueError(h['security']+': minimum allocation requires missing history or excluded security')
                excluded.append({'security': h['security'], 'reason': 'Excluded by membership' if not eligible else f'Only {count} consecutive months'})
        indices = [i for i, k in enumerate(windows) if k]
        if not indices:
            raise ValueError('No holdings have sufficient history before '+start)
        common = min(windows[i] for i in indices)
        X = np.array([[series[keys[i]][m] for i in indices] for m in prior[-common:]])
        c = np.atleast_2d(np.cov(X, rowvar=False, ddof=1))*12
        c = (1-s['covShrink'])*c+s['covShrink']*np.diag(np.diag(c))
        if np.linalg.eigvalsh(c).min() < -1e-10:
            raise ValueError('Covariance is not positive semidefinite')
        means = np.array([np.mean([series[keys[i]][m] for m in prior[-windows[i]:]])*12 for i in indices])
        means = (1-s['meanShrink'])*means+s['meanShrink']*means.mean()
        cov, mu = np.zeros((n, n)), np.zeros(n)
        cov[np.ix_(indices, indices)] = c
        mu[indices], mu[-1] = means, s['cashAnnual']
        bounds = [(h['minimum'], min(h['maximum'], s['cap'])) if windows[i] else (0, 0) for i, h in enumerate(holdings)]
        bounds.append((s['cashMin'], s['cashMax']))
        if sum(a for a,b in bounds) > 1+1e-9 or sum(b for a,b in bounds) < 1-1e-9:
            raise ValueError('Allocation bounds cannot sum to 100%')
        return cov, mu, bounds, windows, excluded

    result = {'version': VERSION, 'keys': keys, 'annual': [], 'monthly': [], 'frontier': [],
              'warnings': ['Research only; not trade instructions.', 'Underlying NAV fees must not be deducted twice.',
                           'Kanaan fees and investor taxes excluded.', 'Volatility target is an estimate, not a future limit.']}
    if s['universe'] == 'Selected':
        result['warnings'].append('Today-selected universe: selection/survivorship bias remains.')
    if s['basis'] == 'Price':
        result['warnings'].append('Price returns exclude cash distributions; accumulating NAVs already retain reinvested income.')
    current = np.array([h['value'] for h in holdings]+[data.get('cashValue', 0.)], dtype=float)
    current = current/current.sum() if current.sum() else np.r_[np.zeros(n-1), 1.]
    periods = months(s['start'], s['end'])
    previous = np.r_[np.zeros(n-1), 1.]
    eq_previous = previous.copy()
    wealth = equal_wealth = peak = 100.
    if s['mode'] == 'Optimise':
        periods = [months(s['end'], str(int(s['end'][:4])+1)+'-12')[1]]
        previous = current.copy()
    active, equal = None, None
    for j, month in enumerate(periods):
        month_number = int(month[5:])
        rebalance = j == 0 or (month_number-1) % s['rebalanceMonths'] == 0
        if rebalance:
            print(f'Estimating and optimising {month} ({j+1}/{len(periods)})', file=sys.stderr, flush=True)
            cov, mu, bounds, windows, excluded = train(month)
            target = solve(mu, cov, bounds, previous, s['risk'], s['cost'])
            active = target.copy()
            equal = np.array([1. if k else 0. for k in windows]+[0.])
            equal /= equal.sum()
            fee = allowance(target, previous, s['cost'])
            eq_fee = allowance(equal, eq_previous, s['cost'])
            result['annual'].append({'month': month, 'weights': target.tolist(), 'historyMonths': windows,
                'excluded': excluded, 'expectedReturn': float(mu@target), 'estimatedRisk': float(np.sqrt(target@cov@target)),
                'cost': fee})
            if j == 0:
                result['currentWeights'] = current.tolist()
                result['currentRisk'] = float(np.sqrt(current@cov@current))
                result['currentReturn'] = float(mu@current)
                for risk in np.linspace(.01, max(.4, s['risk']), 25):
                    try:
                        w = solve(mu, cov, bounds, previous, float(risk), s['cost'])
                        result['frontier'].append({'risk': float(np.sqrt(w@cov@w)), 'expectedReturn': float(mu@w), 'weights': w.tolist()})
                    except ValueError:
                        pass
            if s['mode'] == 'Optimise':
                break
        else:
            fee = eq_fee = 0.
        returns = []
        for i, key in enumerate(keys[:-1]):
            r = series.get(key, {}).get(month)
            if r is None and (active[i] > 1e-10 or equal[i] > 1e-10):
                raise ValueError(f'{key}: missing {s["basis"]} return for {month}; no zero-fill permitted')
            returns.append(r or 0.)
        returns.append(cash)
        r = np.array(returns)
        gross, eq_gross = float(active@r), float(equal@r)
        net, eq_net = (1-fee)*(1+gross)-1, (1-eq_fee)*(1+eq_gross)-1
        wealth *= 1+net
        equal_wealth *= 1+eq_net
        peak = max(peak, wealth)
        result['monthly'].append({'month': month, 'gross': gross, 'cost': fee, 'net': net, 'wealth': wealth,
            'equalNet': eq_net, 'equalWealth': equal_wealth, 'drawdown': wealth/peak-1, 'weights': active.tolist()})
        active = active*(1+r)/(1+gross)
        equal = equal*(1+r)/(1+eq_gross)
        previous, eq_previous = active.copy(), equal.copy()
    result['totalReturn'] = wealth/100-1
    result['cagr'] = (wealth/100)**(12/len(periods))-1 if result['monthly'] else None
    result['volatility'] = float(np.std([m['net'] for m in result['monthly']], ddof=1)*np.sqrt(12)) if len(result['monthly']) > 1 else None
    result['maxDrawdown'] = min([m['drawdown'] for m in result['monthly']]+[0])
    return result

if __name__ == '__main__':
    try:
        print(json.dumps(calculate(json.load(sys.stdin)), allow_nan=False))
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(1)
