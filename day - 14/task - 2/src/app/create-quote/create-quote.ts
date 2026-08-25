import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  Field,
  FieldTree,
  FormField,
  FormRoot,
  ValidationError,
  form,
  maxLength,
  required,
  validate,
} from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import {
  AUTHOR_MAX_LENGTH,
  CreateQuoteApi,
  CreateQuoteFailure,
  CreateQuoteRequest,
  Quote,
  TEXT_MAX_LENGTH,
} from './create-quote-api';

function isBlank(value: string): boolean {
  return value.length > 0 && value.trim().length === 0;
}

@Component({
  selector: 'app-create-quote',
  imports: [FormField, FormRoot],
  templateUrl: './create-quote.html',
  styleUrl: './create-quote.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateQuote {
  private readonly api = inject(CreateQuoteApi);

  protected readonly authorMaxLength = AUTHOR_MAX_LENGTH;
  protected readonly textMaxLength = TEXT_MAX_LENGTH;

  private readonly model = signal<CreateQuoteRequest>({ author: '', text: '' });

  protected readonly createdQuote = signal<Quote | null>(null);

  // The [formRoot] directive on <form> listens for the native submit event itself (and
  // calls .preventDefault()), then invokes this `submission` config - no (ngSubmit) or
  // onSubmit() method needed anywhere in this component or its template.
  protected readonly quoteForm = form(
    this.model,
    (p) => {
      // required() only rejects null/undefined/'' (see isEmpty() in the compiled
      // @angular/forms/signals bundle) - it does NOT trim, so a whitespace-only value
      // passes it. The real API's [Required] attribute *does* trim server-side (verified
      // in Task 1), so this validate() rule is still needed here for the same reason it
      // was needed in the reactive-forms version. See README "Mistake caught" section.
      required(p.author, { message: "Enter the author's name." });
      validate(p.author, ({ value }) =>
        isBlank(value()) ? { kind: 'blank', message: "Enter the author's name." } : undefined,
      );
      maxLength(p.author, AUTHOR_MAX_LENGTH, {
        message: `Must be ${AUTHOR_MAX_LENGTH} characters or fewer.`,
      });

      required(p.text, { message: 'Enter the quote text.' });
      validate(p.text, ({ value }) =>
        isBlank(value()) ? { kind: 'blank', message: 'Enter the quote text.' } : undefined,
      );
      maxLength(p.text, TEXT_MAX_LENGTH, {
        message: `Must be ${TEXT_MAX_LENGTH} characters or fewer.`,
      });
    },
    {
      submission: {
        action: async (field) => {
          this.createdQuote.set(null);
          try {
            const quote = await firstValueFrom(this.api.createQuote(field().value()));
            this.createdQuote.set(quote);
            field().reset({ author: '', text: '' });
            return undefined;
          } catch (error) {
            return this.toSubmitErrors(error as CreateQuoteFailure, field);
          }
        },
        // submit() already calls markAllAsTouched() on every field before checking
        // validity (verified in the compiled source), so error messages are guaranteed
        // visible by the time onInvalid runs - unlike Task 1, there's no need to call
        // markAllAsTouched() here ourselves.
        onInvalid: () => {
          if (this.quoteForm.author().invalid()) {
            this.quoteForm.author().focusBoundControl();
          } else if (this.quoteForm.text().invalid()) {
            this.quoteForm.text().focusBoundControl();
          }
        },
      },
    },
  );

  protected authorErrorMessage(): string | null {
    return this.fieldErrorMessage(this.quoteForm.author);
  }

  protected textErrorMessage(): string | null {
    return this.fieldErrorMessage(this.quoteForm.text);
  }

  // Every validator above sets an explicit `message`, so this is just "first error's
  // message" rather than the kind-based switch Task 1 needed - one of the places Signal
  // Forms is genuinely less code.
  protected rootErrorMessage(): string | null {
    return this.quoteForm().errors()[0]?.message ?? null;
  }

  private fieldErrorMessage(field: Field<string>): string | null {
    const state = field();
    if (!state.invalid() || !state.touched()) {
      return null;
    }
    return state.errors()[0]?.message ?? 'This field is invalid.';
  }

  private toSubmitErrors(
    failure: CreateQuoteFailure,
    field: FieldTree<CreateQuoteRequest>,
  ): ValidationError.WithOptionalFieldTree[] {
    switch (failure.kind) {
      case 'fieldErrors': {
        const errors: ValidationError.WithOptionalFieldTree[] = [];
        if (failure.fieldErrors.author) {
          errors.push({ kind: 'server', message: failure.fieldErrors.author, fieldTree: field.author });
        }
        if (failure.fieldErrors.text) {
          errors.push({ kind: 'server', message: failure.fieldErrors.text, fieldTree: field.text });
        }
        return errors;
      }
      case 'unauthorized':
        return [
          { kind: 'server', message: 'You need to be signed in to add a quote. Please sign in and try again.' },
        ];
      case 'network':
        return [{ kind: 'server', message: 'Could not reach the quotes API. Check your connection and try again.' }];
      case 'serverMessage':
        return [{ kind: 'server', message: failure.message }];
    }
  }
}
