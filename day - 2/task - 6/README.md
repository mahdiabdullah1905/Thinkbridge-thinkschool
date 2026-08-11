# Task 6 - Implement JWT Auth with Custom Issuer

## Objective
Implement JWT authentication and authorization for the Quotes API to secure quote modification endpoints while leaving read endpoints public.

## What Was Implemented

1. **User Model & Database**:
   - Added a `User` entity to track `Id`, `Email`, `PasswordHash`, `RefreshToken`, and `RefreshTokenExpiryTime`.
   - Seeded a default test user `test@example.com` with the password `password123`.

2. **Authentication Flow**:
   - `POST /api/auth/login`: Accepts `Email` and `Password`.
   - Uses `BCrypt.Net-Next` to securely verify the password against the stored hash.
   - On success, issues a short-lived JWT Access Token (valid for 15 minutes) and a generated Refresh Token.

3. **Authorization Rules**:
   - `POST /api/quotes` -> Requires valid JWT token.
   - `DELETE /api/quotes/{id}` -> Requires valid JWT token.
   - `GET /api/quotes` -> Remains public.

4. **Security Considerations**:
   - **Password Hashing**: Uses strong BCrypt hashing; plain-text passwords are never stored.
   - **JWT Key**: Uses the symmetric 256-bit key from `IConfiguration`. In production, this key should be managed securely via environment variables or secret managers, not checked into source control.

## How to Run Curl Tests

1. Start the API locally:
```bash
dotnet run
```

2. Test Public Endpoint (Succeeds):
```bash
curl -i -X GET http://localhost:5000/api/quotes
```

3. Test Protected Endpoint Without Token (Fails with 401 Unauthorized):
```bash
curl -i -X POST http://localhost:5000/api/quotes \
  -H "Content-Type: application/json" \
  -d "{\"author\":\"Test\",\"text\":\"Should fail without auth\"}"
```

4. Login and Get Token:
```bash
curl -i -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"test@example.com\",\"password\":\"password123\"}"
```
*Note the `access_token` from the response.*

5. Test Protected Endpoint With Token (Succeeds with 201 Created):
```bash
# Replace <YOUR_TOKEN> with the actual token
curl -i -X POST http://localhost:5000/api/quotes \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d "{\"author\":\"Test Auth\",\"text\":\"Should succeed with auth\"}"
```
