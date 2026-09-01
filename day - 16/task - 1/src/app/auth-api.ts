import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface LoginRequest {
  email: string;
  password: string;
}

// Matches QuotesApi.Models.AuthResponse (day - 2/QuotesApi/models/AuthModels.cs).
export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

@Injectable({
  providedIn: 'root',
})
export class AuthApi {
  private readonly http = inject(HttpClient);

  // POST is not idempotent - a second use case for proving the retry
  // interceptor skips POST, and the endpoint whose validation failure is
  // used to pin the ValidationProblemDetails contract (it validates the
  // body with no auth or seeded state required).
  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', request);
  }
}
