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

// Matches the body of GET /api/quotes/{id} (QuotesApi.Models.Quote) - confirmed with:
//   curl "http://localhost:5225/api/quotes/1" -> {"id":1,"author":"Authorized","text":"With token","isDeleted":false}
//   curl "http://localhost:5225/api/quotes/999999" -> 404, empty body
// Also the shape of the 201 body from POST /api/quotes (day - 2/QuotesApi/Extensions/ProgramExtensions.cs
// returns Results.Created($"/api/quotes/{quote.Id}", quote) - the persisted Quote entity, same shape).
export interface QuoteDetail {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
}

// Matches QuotesApi.Models.CreateQuoteRequest (day - 2/QuotesApi/models/CreateQuoteRequest.cs):
//   Author: [Required][StringLength(100, MinimumLength = 1)]
//   Text:   [Required][StringLength(1000, MinimumLength = 1)]
export interface CreateQuoteRequest {
  author: string;
  text: string;
}

export const AUTHOR_MAX_LENGTH = 100;
export const TEXT_MAX_LENGTH = 1000;

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

  getQuoteById(id: number): Observable<QuoteDetail> {
    return this.http.get<QuoteDetail>(`/api/quotes/${id}`);
  }

  // POST /api/quotes carries .RequireAuthorization() (ProgramExtensions.cs) - an
  // anonymous request 401s before ever reaching CreateQuoteCommandHandler. The
  // authInterceptor attaches the Bearer token when one is present; the caller
  // (the "quotes/new" route) is guarded so this is only reachable when signed in.
  createQuote(request: CreateQuoteRequest): Observable<QuoteDetail> {
    return this.http.post<QuoteDetail>('/api/quotes', request);
  }
}
