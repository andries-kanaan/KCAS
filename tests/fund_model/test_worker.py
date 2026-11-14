import importlib.util
import copy
import unittest
from pathlib import Path
import numpy as np

spec=importlib.util.spec_from_file_location('worker',Path(__file__).resolve().parents[2]/'src/KCAS.Admin/FundModel/worker.py')
worker=importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)

def example():
    rng=np.random.default_rng(42)
    series=[]
    for name in ['A','B','C']:
        price=100.
        observations=[]
        for month in worker.months('2016-12','2024-12'):
            price*=1+float(rng.normal(.008,.025))
            observations.append({'month':month,'price':price,'totalReturnIndex':price,'source':'Synthetic test'})
        series.append({'key':name,'currency':'USD','series':observations})
    return {'currency':'USD','securities':series,'holdings':[{'security':s['key'],'value':100,'minimum':0,'maximum':.5,'included':True} for s in series],
            'scenario':{'name':'Test','risk':.13,'cap':.5,'cashMin':0,'cashMax':1,'cashAnnual':0,'covShrink':.25,'meanShrink':.5,'cost':.001,'window':60,'minimumHistory':36,'rebalanceMonths':12,'start':'2022-03','end':'2024-12','basis':'Price','mode':'Backtest','universe':'Selected'}}

class WorkerTests(unittest.TestCase):
    def test_constraints_drift_and_compounding(self):
        r=worker.calculate(example())
        self.assertEqual(len(r['annual']),3)
        self.assertEqual(len(r['monthly']),34)
        for a in r['annual']:
            self.assertAlmostEqual(sum(a['weights']),1,places=7)
            self.assertLessEqual(a['estimatedRisk'],.1300001)
            self.assertLessEqual(max(a['weights'][:-1]),.5000001)
        self.assertAlmostEqual(np.prod([1+m['net'] for m in r['monthly']])-1,r['totalReturn'],places=9)
        self.assertNotEqual(r['monthly'][0]['weights'],r['monthly'][1]['weights'])
    def test_no_lookahead(self):
        d=example();a=worker.calculate(d)['annual'][0]['weights']
        for s in d['securities']:
            for p in s['series']:
                if p['month']>='2022-03':p['price']*=2
        b=worker.calculate(d)['annual'][0]['weights']
        np.testing.assert_allclose(a,b,atol=1e-7)
    def test_missing_not_zero_filled(self):
        d=example();d['securities'][0]['series']=[p for p in d['securities'][0]['series'] if p['month']!='2023-05']
        with self.assertRaisesRegex(ValueError,'missing'):worker.calculate(d)
    def test_infeasible_limits(self):
        d=example();d['scenario']['cap']=.15;d['scenario']['cashMax']=0
        with self.assertRaisesRegex(ValueError,'100%'):worker.calculate(d)
    def test_total_returns_and_foreign_currency(self):
        d=example();d['scenario']['basis']='TotalReturn'
        self.assertGreater(worker.calculate(d)['totalReturn'],0)
        d['securities'][0]['currency']='ZAR'
        with self.assertRaisesRegex(ValueError,'FX'):worker.calculate(d)

if __name__=='__main__':unittest.main()
