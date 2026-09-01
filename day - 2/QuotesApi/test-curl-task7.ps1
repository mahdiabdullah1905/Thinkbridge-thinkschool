$ErrorActionPreference = "Stop"

Write-Host "==========================="
Write-Host "1. Login"
Write-Host "==========================="
$loginResponse = Invoke-RestMethod -Uri "http://localhost:5225/api/auth/login" -Method Post -Headers @{"Content-Type"="application/json"} -Body '{"email":"test@example.com","password":"password123"}'
$tokenA = $loginResponse.refreshToken
Write-Host "Got Refresh Token A: $($tokenA.Substring(0, 10))..."

Write-Host "`n==========================="
Write-Host "2. Refresh using Token A"
Write-Host "==========================="
$refresh1Response = Invoke-RestMethod -Uri "http://localhost:5225/api/auth/refresh" -Method Post -Headers @{"Content-Type"="application/json"} -Body "{ `"refreshToken`": `"$tokenA`" }"
$tokenB = $refresh1Response.refreshToken
Write-Host "Success: Got Refresh Token B: $($tokenB.Substring(0, 10))..."

Write-Host "`n==========================="
Write-Host "3. Try old Token A again (Reuse detection)"
Write-Host "==========================="
try {
    Invoke-RestMethod -Uri "http://localhost:5225/api/auth/refresh" -Method Post -Headers @{"Content-Type"="application/json"} -Body "{ `"refreshToken`": `"$tokenA`" }"
    Write-Host "FAIL: Did not get 401"
} catch {
    Write-Host "Success: Got $($_.Exception.Response.StatusCode)"
}

Write-Host "`n==========================="
Write-Host "4. Try Token B (Family should be revoked)"
Write-Host "==========================="
try {
    Invoke-RestMethod -Uri "http://localhost:5225/api/auth/refresh" -Method Post -Headers @{"Content-Type"="application/json"} -Body "{ `"refreshToken`": `"$tokenB`" }"
    Write-Host "FAIL: Did not get 401"
} catch {
    Write-Host "Success: Got $($_.Exception.Response.StatusCode)"
}

Write-Host "`n==========================="
Write-Host "5. Login again to get Token C"
Write-Host "==========================="
$login2Response = Invoke-RestMethod -Uri "http://localhost:5225/api/auth/login" -Method Post -Headers @{"Content-Type"="application/json"} -Body '{"email":"test@example.com","password":"password123"}'
$tokenC = $login2Response.refreshToken
Write-Host "Got Refresh Token C: $($tokenC.Substring(0, 10))..."

Write-Host "`n==========================="
Write-Host "6. Logout using Token C"
Write-Host "==========================="
Invoke-RestMethod -Uri "http://localhost:5225/api/auth/logout" -Method Post -Headers @{"Content-Type"="application/json"} -Body "{ `"refreshToken`": `"$tokenC`" }"
Write-Host "Logged out."

Write-Host "`n==========================="
Write-Host "7. Try Token C (Should be revoked)"
Write-Host "==========================="
try {
    Invoke-RestMethod -Uri "http://localhost:5225/api/auth/refresh" -Method Post -Headers @{"Content-Type"="application/json"} -Body "{ `"refreshToken`": `"$tokenC`" }"
    Write-Host "FAIL: Did not get 401"
} catch {
    Write-Host "Success: Got $($_.Exception.Response.StatusCode)"
}

Write-Host "`nDone."
