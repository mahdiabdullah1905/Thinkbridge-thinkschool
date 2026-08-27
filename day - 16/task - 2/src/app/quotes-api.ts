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

  getQuoteById(id: number): Observable<QuoteDetail> {
    return this.http.get<QuoteDetail>(`/api/quotes/${id}`);
  }
}
