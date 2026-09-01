# Task 7 - Refresh Tokens with Rotation

## Objective
Implement secure refresh-token rotation to mitigate the risks of token theft and leakage, ensuring that refresh tokens are single-use and that token families are tracked for reuse detection.

## What Was Implemented

### 1. Dedicated RefreshToken Entity
Refresh tokens are now stored in their own database table (`RefreshTokens`) linked to the `User`. The obsolete `RefreshToken` fields were removed from the `User` entity to prevent duplicate tracking systems.

### 2. Token Security & Hashing
- **Generation:** Refresh tokens are generated using a cryptographically secure random number generator (`System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)`), guaranteeing 256 bits of entropy.
- **Hashing:** The raw tokens are **never** stored in the database. Instead, they are hashed using SHA-256 before storage. Since they possess extremely high entropy, SHA-256 is mathematically secure and prevents rainbow-table attacks. 
- **Replacement Tracking:** We use the `ReplacedByTokenHash` field to track the rotation chain. It stores the *hash* of the new token to avoid storing raw secrets anywhere.

### 3. Token Rotation Flow
When the client calls `POST /api/auth/refresh` with a valid refresh token:
1. A new 15-minute access token and a new 7-day refresh token are generated.
2. The old refresh token is marked as revoked (`RevokedAt = UtcNow`) and linked to the hash of the new token (`ReplacedByTokenHash`).
3. The new token is saved. Both tokens share the same `FamilyId`.

### 4. Token Reuse Detection
Tokens are single-use. If a token is presented that already has `ReplacedByTokenHash` populated, it means the token was already rotated. This indicates that either the token was stolen or the client is malfunctioning.
- **Action:** The system treats this as theft, immediately revokes the **entire token family** (all tokens sharing the same `FamilyId`), and forces the user to authenticate again.
- **Security Logging:** A security warning is logged identifying the reused `FamilyId`.

### 5. Logout
The `POST /api/auth/logout` endpoint accepts a refresh token and revokes it. This revocation is distinguished from rotation revocation because `ReplacedByTokenHash` remains `null`. If presented again, the token is simply rejected without triggering a full family-theft alert.

## How to Test Manually
A PowerShell test script (`test-curl-task7.ps1`) is provided to automate these verifications:
1. **Login:** Obtain a refresh token (Token A).
2. **Valid Refresh:** Use Token A to get Token B.
3. **Reuse detection:** Try to use Token A again. It fails (401), and as a result, the entire family is revoked.
4. **Family revocation:** Try to use Token B. It fails (401) because the family was revoked.
5. **Logout:** Login to get Token C, then logout using it. Verify it fails (401) if tried again.
