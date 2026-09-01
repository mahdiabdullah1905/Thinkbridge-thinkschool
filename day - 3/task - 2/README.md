# Day 3, Task 2: Authorization Policies and Claims

## Overview
Because of an ongoing HR access blocker delaying our Azure/Entra setup, **Task 1 has been skipped**. This implementation for Task 2 proceeds using our Day 2 JWT authentication (or test mock authentication) to demonstrate robust authorization using claims and policies.

## What Are Authorization Policies?
In ASP.NET Core, **Authentication** identifies who the user is (their claims and identity), while **Authorization** determines what they are allowed to do. 
Rather than hardcoding role checks (e.g., `if (user.IsInRole("Admin"))`) inside our controllers or endpoints, **Authorization Policies** allow us to decouple authorization logic from our endpoints. We can define a policy with rules (like requiring specific claims, roles, or custom requirements) and apply it generically across our application using `[Authorize(Policy = "PolicyName")]` or `.RequireAuthorization("PolicyName")`.

## The `can-edit-quotes` Policy
We created a policy named `can-edit-quotes`. This policy requires the user to possess a specific claim: `scope = quotes.write`. 
- **Usage**: The policy is applied directly to the POST and DELETE endpoints of the Quotes API.
- **Outcome**: Only clients holding a token with `scope: quotes.write` can successfully invoke these mutating operations.

## Custom Requirement and Handler
We also demonstrated a custom, resource-based authorization requirement: `SameAuthorRequirement` and `SameAuthorHandler`.
- **Why?** Sometimes, authorization depends on the specific resource being accessed. A claim-based policy isn't enough if we need to know *which* quote is being modified.
- **How it Works**: The handler retrieves the requested Quote resource and compares the Quote's `Author` field to the user's `Email` claim. 
- **Result**: Even if a user has the `can-edit-quotes` policy, they can only delete a Quote if they were the original author of that Quote.

## 401 Unauthorized vs 403 Forbidden
Understanding the difference is critical for API design:
- **401 Unauthorized**: The server does not know who the user is. The client either didn't send an authentication token, or the token was invalid/expired. *(Authentication failed)*
- **403 Forbidden**: The server knows exactly who the user is, but the user does not have the necessary permissions (claims, roles, or resource ownership) to perform the action. *(Authorization failed)*
