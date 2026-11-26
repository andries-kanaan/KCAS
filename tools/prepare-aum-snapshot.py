"""Prepare a private KCAS CAR scenario snapshot from the verified November workbook.

Read-only source; requires openpyxl. The JSON contains ordinal IDs, not client
names, dates of birth or account numbers. It still contains private balances.
"""

import argparse
import hashlib
import json
import re
from pathlib import Path

from openpyxl import load_workbook


def require(condition, message):
    if not condition:
        raise ValueError(message)


def rows(book, name, start, count, columns):
    return list(book[name].iter_rows(min_row=start, max_row=start + count - 1,
                                    max_col=columns, values_only=True))


def prepare(source):
    values = load_workbook(source, read_only=True, data_only=True, keep_links=False)
    formulas = load_workbook(source, read_only=True, keep_links=False)
    try:
        return extract(values, formulas, source)
    finally:
        values.close()
        formulas.close()


def extract(v, f, source):
    inputs = v["Inputs"]
    require(inputs["A72"].value == "Growth assumptions | EO and IBKR stay separate",
            "Expected the verified separate EO/IBKR workbook layout.")
    controls = v["Scenario controls"]
    fees = v["PDF terms"]
    local = v["SA PDF estimates"]
    coverage = v["Scenario comparison"]
    require(controls["B10"].value == 1, "The snapshot requires the CPI hurdle active.")
    require(inputs["B20"].value == 12, "Expected 12 months per projection year.")
    names = rows(v, "Fund monthly", 6, 35, 7)
    defaults = {r[0]: r[1] for r in rows(v, "Inputs", 77, 35, 4)}
    funds = [
        dict(name=n, car=defaults.get(n, defaults["Moriah Global"]),
             basis="Historical net CAR proxy; legacy PDF gross-up" if n == "Equity Offshore"
             else "Historical net CAR basis", saPdf=n in {"Compulsory SA", "Equity SA", "Global SA"})
        for _, _, n, *_ in names
    ]
    funds.append(dict(name="EO IBKR Brokers", car=defaults["EO IBKR Brokers"],
                      basis="Broker CAR before EO wrapper fees"))
    fund_index = {x["name"]: i for i, x in enumerate(funds)}
    source_holdings = rows(v, "Opening holdings", 6, 707, 19)
    flows = rows(v, "Cashflow sources", 6, 286, 19)
    flows = [r for r in flows if r[4]]
    account_index = {r[4]: i for i, r in enumerate(flows)}
    require(len(account_index) == len(flows), "Duplicate cash flow accounts.")
    pool_index = {name: i for i, name in enumerate(dict.fromkeys(r[0] for r in flows))}
    account_rules = rows(f, "Account bases", 6, len(flows), 4)
    accounts = []
    for r, a in zip(flows, account_rules):
        require(r[4] == a[1], "Account order differs.")
        annual = max(0, (r[9] or 0) - (r[14] or 0)) * 12 / r[11] if r[6] == "Fixed" else 0
        rate = (r[10] or 0) if r[6] == "Rate" else 0
        if a[3] == "=0":
            annual = rate = 0
        else:
            require(isinstance(a[3], str) and a[3].startswith("=IF('Cashflow sources'!G"), "Unrecognised withdrawal rule.")
        accounts.append(dict(pool=pool_index[r[0]], fixedAnnualWithdrawal=annual,
                             withdrawalRate=rate, interval=r[11], advance=r[12] == 1))
    holdings = [
        dict(fund=fund_index[r[7]], account=account_index[r[4]],
             pool=accounts[account_index[r[4]]]["pool"], opening=r[9], incomeWeight=r[16])
        for r in source_holdings
    ]
    retirements = [0] * len(pool_index)
    pool_count = len(pool_index)
    for month, pool, _, _, _, retirement, _ in rows(f, "Pool forecast", 6, 60 * pool_count, 7):
        if isinstance(retirement, str) and re.fullmatch(r"=C\d+", retirement):
            i = pool_index[pool]
            if not retirements[i]:
                retirements[i] = month

    support_rules = rows(f, "Support monthly", 6, 60 * 13, 11)
    support_values = rows(v, "Client support", 6, 13, 4)
    rules = rows(f, "Holding forecast", 6, 60 * len(holdings), 19)
    amounts = rows(v, "Holding forecast", 6, 60 * len(holdings), 9)
    targets = [[] for _ in support_values]
    for i, r in enumerate(rules[:len(holdings)]):
        for sr in re.findall(r"'Support monthly'![IJ](\d+)", r[17] or ""):
            targets[int(sr) - 6].append(i)
    supports = []
    for j, (_, kind, _, annual) in enumerate(support_values):
        scope = [int(x) - 6 for x in re.findall(r"F(\d+)", support_rules[j][3])]
        require(scope and targets[j], "Missing support scope or destination.")
        supports.append(dict(income=kind == "Income", pool=holdings[scope[0]]["pool"],
                             scope=scope, targets=targets[j], annualAmount=annual))

    mortality = rows(v, "Mortality monthly", 6, 60 * (v["Mortality clients"].max_row - 5), 13)
    costs = rows(v, "Monthly forecast", 6, 60, 21)
    periods = []
    for m in range(60):
        contributions, withdrawal, estate, survival = [], [], [], []
        for i, h in enumerate(holdings):
            r = rules[m * len(holdings) + i]
            require(r[0] == m + 1 and r[2] == source_holdings[i][4], "Holding order differs.")
            weight = re.search(r"'Mortality monthly'!([GM])(\d+)$", r[9])
            require(weight is not None, "Unknown withdrawal mortality factor.")
            pool_rate = 0 < retirements[h["pool"]] <= m + 1
            require(("'Pool forecast'!" in r[9]) == pool_rate, "Retirement rule differs.")
            withdrawal.append(mortality[int(weight[2]) - 6][6 if weight[1] == "G" else 12])
            death = re.search(r"'Mortality monthly'!L(\d+)$", r[18])
            require(death is not None or r[18] == "=0", "Unknown estate withdrawal rule.")
            estate.append(mortality[int(death[1]) - 6][11] if death else 0)
            contributions.append(amounts[m * len(holdings) + i][8])
        for j, item in enumerate(supports):
            formula = support_rules[m * len(supports) + j][8 if item["income"] else 9]
            sr = re.search(r"'Mortality monthly'!G(\d+)$", formula)
            require(sr is not None, "Unknown support survival factor.")
            survival.append(mortality[int(sr[1]) - 6][6])
        periods.append(dict(costs=costs[m][6], contributions=contributions,
                            withdrawalWeights=withdrawal, estateWeights=estate, supportSurvival=survival))

    terms = [
        dict(nav=r[2], highWater=r[3], hurdleBase=r[2] if i < 2 else r[4], rate=r[5], hurdle=r[6])
        for i, r in enumerate(rows(v, "PDF terms", 6, 3, 7))
    ]
    with source.open("rb") as stream:
        fingerprint = hashlib.file_digest(stream, "sha256").hexdigest()
    return dict(
        version=1, source=source.name, sourceHash=fingerprint, asOf=inputs["B19"].value.date().isoformat(),
        brokerShare=inputs["B113"].value, brokerWrapperCost=v["IB returns"]["B26"].value,
        monthlyFeeIncome=inputs["B8"].value - fees["B13"].value, feeBaseAum=inputs["B7"].value,
        pdfIncomeShare=fees["B12"].value, offshorePdfEnabled=fees["B14"].value,
        retirementRate=inputs["B65"].value, supportRate=inputs["B62"].value, supportThreshold=inputs["B61"].value,
        usCpi2025=controls["B9"].value, futureUsCpi=controls["B8"].value,
        saHurdle=local["B6"].value + local["B10"].value,
        saPdfRate=local["B7"].value * local["B8"].value * local["B9"].value,
        recoveryMonth=inputs["B66"].value, recoveryShare=inputs["B67"].value, recoveryCar=inputs["B68"].value,
        businessBaseline=dict(date=inputs["B6"].value.date().isoformat(), monthlyOperatingCosts=inputs["B9"].value,
                              monthlySalary=inputs["B56"].value, salaryIncrease=inputs["B57"].value,
                              firstSalaryIncrease=inputs["B58"].value.date().isoformat(),
                              otherMonthlyOperatingCosts=inputs["B59"].value, monthlyIncomeSupport=inputs["B13"].value,
                              annualFeeSupport=inputs["B14"].value),
        mortalityBasis=dict(withdrawalProbability=controls["B6"].value, delayMonths=controls["B7"].value,
                            eligibleVoluntary=coverage["B15"].value, unprovenJoint=coverage["B16"].value,
                            totalVoluntary=coverage["B18"].value, unclassified=coverage["B22"].value),
        funds=funds, holdings=holdings, accounts=accounts, retirementMonths=retirements,
        supports=supports, feeTerms=terms, periods=periods,
    )


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workbook", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    snapshot = prepare(args.workbook)
    with args.output.open("x", encoding="utf-8") as file:
        json.dump(snapshot, file, separators=(",", ":"), allow_nan=False)
    print(f"Prepared {len(snapshot['holdings'])} holdings, {len(snapshot['periods'])} months, as of {snapshot['asOf']}.")
