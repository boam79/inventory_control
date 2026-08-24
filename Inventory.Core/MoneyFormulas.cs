namespace Inventory.Core;

/// <summary>
/// 재고·입출고 금액: 수량 × 단가. 재고 단가는 이동평균(로트 원가, 없으면 기준가).
/// </summary>
public static class MoneyFormulas
{
    public static decimal LineAmount(decimal quantity, decimal unitPrice) => quantity * unitPrice;

    /// <summary>
    /// 로트 원가 합이 있으면 그 합÷현재고, 원가 0 로트는 기준가로 평가. 재고금액 = 현재고 × 단가.
    /// </summary>
    public static (decimal UnitCost, decimal StockValue) StockValuation(
        decimal onHand,
        decimal lotValue,
        decimal zeroCostQty,
        decimal fallbackUnitCost)
    {
        if (onHand <= 0)
        {
            return (fallbackUnitCost, 0m);
        }

        var value = lotValue + (zeroCostQty * fallbackUnitCost);
        var unitCost = value / onHand;
        return (unitCost, LineAmount(onHand, unitCost));
    }

    public static string FormatWon(decimal value)
    {
        var rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == decimal.Truncate(rounded) ? $"{rounded:N0}원" : $"{rounded:N2}원";
    }

    public static string FormatQty(decimal value) =>
        value == decimal.Truncate(value) ? $"{value:N0}" : $"{value:N3}";
}
