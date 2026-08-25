import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

// Matches QuotesApi.Models.CreateQuoteRequest (day - 2/QuotesApi/models/CreateQuoteRequest.cs):
//   Author: [Required][StringLength(100, MinimumLength = 1)]
//   Text:   [Required][StringLength(1000, MinimumLength = 1)]
export interface CreateQuoteRequest {
  author: string;
  text: string;
}

export const AUTHOR_MAX_LENGTH = 100;
export const TEXT_MAX_LENGTH = 1000;

// Matches QuotesApi.Models.Quote (day - 2/QuotesApi/models/Quotes.cs) as serialized on the
// Results.Created(...) response from POST /api/quotes.
export interface Quote {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
}

type CreateQuoteFieldErrors = Partial<Record<keyof CreateQuoteRequest, string>>;

// Same failure taxonomy as the Task 1 (reactive forms) version - this classification is
// dictated by the real API's behavior (day - 2/QuotesApi/Extensions/ProgramExtensions.cs),
// not by which form library is consuming it. What differs in this task is what happens to
// a CreateQuoteFailure once the component has it - see create-quote.ts's toSubmitErrors,
// which targets Signal Forms fields instead of calling control.setErrors().
export type CreateQuoteFailure =
  | { kind: 'fieldErrors'; fieldErrors: CreateQuoteFieldErrors }
  | { kind: 'unauthorized' }
  | { kind: 'serverMessage'; message: string }
  | { kind: 'network' };

// Body shape produced by ValidationFilter<CreateQuoteRequest>
// (day - 2/QuotesApi/Filters/ValidationFilter.cs) via TypedResults.ValidationProblem
// when a [Required]/[StringLength] check fails.
interface ValidationProblemBody {
  errors?: Record<string, string[]>;
}

// Body shape returned by MapQuoteEndpoints' POST handler
// (day - 2/QuotesApi/Extensions/ProgramExtensions.cs) via Results.BadRequest(new
// ProblemDetails { ... }) when the DTO passes annotation checks but Quote.Create still
// rejects it.
interface ProblemDetailsBody {
  title?: string;
  detail?: string;
}

@Injectable({ providedIn: 'root' })
export class CreateQuoteApi {
  private readonly http = inject(HttpClient);

  // POST http://localhost:5225/api/quotes, proxied from /api in dev (see proxy.conf.json)
  // so the browser never has to cross origins.
  createQuote(request: CreateQuoteRequest): Observable<Quote> {
    return this.http
      .post<Quote>('/api/quotes', request)
      .pipe(catchError((error: HttpErrorResponse) => throwError(() => this.toFailure(error))));
  }

  private toFailure(error: HttpErrorResponse): CreateQuoteFailure {
    if (error.status === 0) {
      return { kind: 'network' };
    }

    // POST /api/quotes carries .RequireAuthorization() (ProgramExtensions.cs) - an
    // anonymous request is rejected before it ever reaches CreateQuoteCommandHandler.
    if (error.status === 401) {
      return { kind: 'unauthorized' };
    }

    const body = error.error as (ValidationProblemBody & ProblemDetailsBody) | null;

    if (body?.errors) {
      const fieldErrors: CreateQuoteFieldErrors = {};
      for (const [key, messages] of Object.entries(body.errors)) {
        const field = key.toLowerCase();
        if ((field === 'author' || field === 'text') && messages.length > 0) {
          fieldErrors[field] = messages[0];
        }
      }
      if (Object.keys(fieldErrors).length > 0) {
        return { kind: 'fieldErrors', fieldErrors };
      }
    }

    if (body?.detail) {
      return { kind: 'serverMessage', message: body.detail };
    }

    return { kind: 'serverMessage', message: `Request failed with status ${error.status}.` };
  }
}
