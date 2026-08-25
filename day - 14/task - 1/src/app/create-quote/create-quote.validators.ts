import { AbstractControl, ValidationErrors } from '@angular/forms';

// Verified against the running API: [Required] on CreateQuoteRequest.Author/Text
// (day - 2/QuotesApi/models/CreateQuoteRequest.cs) trims before checking length, so a
// whitespace-only value like "   " is rejected server-side with "The Author field is
// required." This validator gives the same rejection instantly, client-side, instead of
// round tripping to the server for it.
export function notBlankValidator(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value;
  return value.length > 0 && value.trim().length === 0 ? { blank: true } : null;
}
