import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Matches QuotesApi.Models.QuoteListItem (day - 2/QuotesApi/models/QuoteListItem.cs)
export interface QuoteListItem {
  id: number;
  author: string;
  textPreview: string;
  authorQuoteCount: number;
}

// Matches QuotesApi.Models.PaginatedResponse<T> (day - 2/QuotesApi/models/PaginatedResponse.cs)
export interface PaginatedResponse<T> {
  page: number;
  size: number;
  totalCount: number;
  items: T[];
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
}
