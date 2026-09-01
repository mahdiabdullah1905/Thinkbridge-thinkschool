import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Matches QuotesApi.Models.QuoteListItem (day - 2/QuotesApi/models/QuoteListItem.cs) -
// confirmed against the running API with:
//   curl "http://localhost:5225/api/quotes?page=1&size=2"
//   -> {"page":1,"size":2,"totalCount":6,"items":[{"id":1,"author":"Authorized","textPreview":"With token","authorQuoteCount":1}, ...]}
export interface QuoteListItem {
  id: number;
  author: string;
  textPreview: string;
  authorQuoteCount: number;
}

// Matches QuotesApi.Models.PaginatedResponse<T> (day - 2/QuotesApi/models/PaginatedResponse.cs).
export interface PaginatedResponse<T> {
  page: number;
  size: number;
  totalCount: number;
  items: T[];
}

export interface CreateQuoteRequest {
  author: string;
  text: string;
}

// Matches the 201 body from POST /api/quotes, which returns the persisted
// Quote entity (QuotesApi.Models.Quote), not a QuoteListItem.
export interface QuoteDetail {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
}

@Injectable({
  providedIn: 'root',
})
export class QuotesApi {
  private readonly http = inject(HttpClient);

  getQuotes(page: number, size: number): Observable<PaginatedResponse<QuoteListItem>> {
    return this.http.get<PaginatedResponse<QuoteListItem>>('/api/quotes', {
      params: { page, size },
    });
  }

  // POST is not idempotent - used to prove the retry interceptor leaves it alone.
  createQuote(request: CreateQuoteRequest): Observable<QuoteDetail> {
    return this.http.post<QuoteDetail>('/api/quotes', request);
  }
}
