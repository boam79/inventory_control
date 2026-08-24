using Inventory.Core;

namespace Inventory.Tests;

public class MoneyFormulasTests
{
    [Fact]
    public void Line_amount_is_quantity_times_unit_price()
    {
        Assert.Equal(4_296_000m, MoneyFormulas.LineAmount(10740m, 400m));
        Assert.Equal(80m, MoneyFormulas.LineAmount(1m, 80m));
        Assert.Equal(4_500m, MoneyFormulas.LineAmount(1m, 4500m));
    }

    [Fact]
    public void Stock_valuation_on_hand_times_unit_cost_equals_stock_value()
    {
        var (unitCost, stockValue) = MoneyFormulas.StockValuation(10740m, 4_296_000m, 0m, 400m);
        Assert.Equal(400m, unitCost);
        Assert.Equal(MoneyFormulas.LineAmount(10740m, unitCost), stockValue);
    }
}
