## Refactoring Notes



## 1. God Method

The PostOrder method is doing too much work . This makes the method massive and very difficult to test.

 Intended Fix: Pull the core business logic into a separate class.



## 2. Direct DbContext Access

OrderController directly uses AppDbContext to access the database. This makes the controller tightly coupled to EF Core and makes testing it in isolation more difficult.

 Intended Fix :  move the databse object into a different repository.



## 3. Empty Catch Blocks

There are four different catch  blocks that doesnt  exceptions  If something fails we won't know about it.  We'll have no logs to figure out what happened.

Intended Fix: Get rid of the empty catches. We should handle exceptions that occur and only catch specific exceptions we know how to handle.



## 4. Synchronous EF Calls

The method is async, but some database operations like SaveChanges() are still synchronous. This can block the thread while waiting for the database.

Intended Fix:  Switch all DB operations to their async versions  and await them.



## 5. hardcoded Numbers

There are hardcoded numbers  like checking cust.Status == 1 to see if a customer is active, or grandTotal > 500  for a discount. If the active status ID changes in the database, we'd have to hunt down every 1 in the code.

Intended Fix: Replace these with enums like CustomerStatus.Active or configuration constants.



## 6. Hardcoded Strings

strings like `"ELEC"`, `"VIP"`, `"PENDING"`, and `"UNPAID"` are hardcoded throughout the method. A simple typo here could easily break the tax or discount logic.

Intended Fix: Use enums or constants for categories, customer types, and order status.



## 7. Null Dereference Bug

Around line 47, the code tries to read the zip code with `string zip = req. ShippingDetails.ZipCode; without ever checking if req.ShippingDetails itself is null. If a client sends a payload without shipping details, this will throw a  NullReferenceException and crash the request.

Intended Fix: Add a null check before accessing properties on nested objects, or handle it via upfront model validation.



## 8. wrong loop 

The loop over the request items uses <= req.Items.Count ,

for (int i = 0; i <= req.Items.Count; i++)

This means the loop cause IndexOutOfRangeException . Right now, it's just being swallowed by the first empty catch block.

Intended Fix:Change the condition to < req.Items.Count.



## 9. Duplicated Validation

The exact same string validation logic string.IsNullOrEmpty is written out manually for ShippingDetails and then copypasted for BillingDetails .

Intended Fix: Extract the address validation into a reusable private method .





## 10. Repeated Database Queries

Inside the item processing loop, the code queries the database for each individual product: if an order has 50 items, that's 50 separate database calls.

Intended Fix: Gather all the required product IDs upfront and fetch them in a single query before starting the loop.