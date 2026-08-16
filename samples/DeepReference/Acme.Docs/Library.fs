namespace Acme.Docs

open FsLiveDocs

/// <summary>A customer order used throughout the deep-reference documentation.</summary>
type Order = {
    Id: int
    Subtotal: decimal
}

/// <summary>Functions for constructing and pricing orders.</summary>
module Order =

    // <snippet:CreateOrder>
    /// <summary>Creates an order after validating its subtotal.</summary>
    /// <param name="id">Stable order identifier.</param>
    /// <param name="subtotal">Price before tax.</param>
    /// <returns>A validated order.</returns>
    let create id subtotal =
        if subtotal < 0M then invalidArg (nameof subtotal) "Subtotal cannot be negative."
        { Id = id; Subtotal = subtotal }
    // </snippet:CreateOrder>

    /// <summary>Calculates the total including a fractional tax rate.</summary>
    /// <example name="CalculateTotal" data-livedocs="snapshot">
    /// > Acme.Docs.Order.create 7 20M |> Acme.Docs.Order.total 0.1M;;
    /// val it: decimal = 22.0M
    /// </example>
    let total taxRate order = order.Subtotal * (1M + taxRate)

/// <summary>State used to demonstrate an explicit documentation-test scenario.</summary>
module CustomerContext =
    let mutable private discount = 0M

    /// <summary>Loads the deterministic preferred-customer fixture.</summary>
    [<DocScenario("preferred-customer")>]
    let loadPreferredCustomer () =
        discount <- 0.1M

    /// <summary>Applies the currently loaded customer's discount.</summary>
    /// <example name="PreferredCustomerPrice" scenario="preferred-customer" data-livedocs="snapshot">
    /// > Acme.Docs.CustomerContext.price 100M;;
    /// val it: decimal = 90.0M
    /// </example>
    let price subtotal = subtotal * (1M - discount)
