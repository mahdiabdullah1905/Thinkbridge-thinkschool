import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AppError } from '../../core/errors/app-error';
import { AUTHOR_MAX_LENGTH, QuoteDetail, TEXT_MAX_LENGTH } from '../../core/quotes-api';
import { QuotesStore } from '../../state/quotes-store/quotes-store';
import { notBlankValidator } from './create-quote.validators';

type SubmitStatus = 'idle' | 'submitting' | 'success' | 'error';
type FieldName = 'author' | 'text';

// ASP.NET's ValidationProblemDetails keys errors by the C# property name
// ("Author"/"Text"), not the lowercase formControlName - confirmed against
// the real API's 400 body. Match case-insensitively rather than assuming.
function fieldMessage(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
  field: FieldName,
): string | undefined {
  const key = Object.keys(fieldErrors).find((k) => k.toLowerCase() === field);
  return key ? fieldErrors[key][0] : undefined;
}

@Component({
  selector: 'app-create-quote',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './create-quote.html',
  styleUrl: './create-quote.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateQuote {
  // HTTP + the "refresh the list" side effect live in the store (see
  // QuotesStore.createQuote); this component owns only form-local UX:
  // client-side validation, focus management, and rendering whatever
  // AppError the store's Observable rejects with.
  private readonly store = inject(QuotesStore);

  private readonly authorInput = viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput = viewChild<ElementRef<HTMLTextAreaElement>>('textInput');

  protected readonly authorMaxLength = AUTHOR_MAX_LENGTH;
  protected readonly textMaxLength = TEXT_MAX_LENGTH;

  protected readonly form = new FormGroup({
    author: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, notBlankValidator, Validators.maxLength(AUTHOR_MAX_LENGTH)],
    }),
    text: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, notBlankValidator, Validators.maxLength(TEXT_MAX_LENGTH)],
    }),
  });

  protected readonly status = signal<SubmitStatus>('idle');
  protected readonly serverMessage = signal<string | null>(null);
  protected readonly createdQuote = signal<QuoteDetail | null>(null);

  protected authorErrorMessage(): string | null {
    return this.errorMessageFor(this.form.controls.author, "the author's name", this.authorMaxLength);
  }

  protected textErrorMessage(): string | null {
    return this.errorMessageFor(this.form.controls.text, 'the quote text', this.textMaxLength);
  }

  protected onSubmit(): void {
    if (this.status() === 'submitting') {
      return;
    }

    this.serverMessage.set(null);
    this.createdQuote.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidField();
      return;
    }

    this.status.set('submitting');
    const { author, text } = this.form.getRawValue();

    this.store.createQuote({ author, text }).subscribe({
      next: (quote) => {
        this.status.set('success');
        this.createdQuote.set(quote);
        this.form.reset({ author: '', text: '' });
      },
      error: (err: AppError) => {
        this.status.set('error');
        this.applyFailure(err);
      },
    });
  }

  private applyFailure(err: AppError): void {
    if (err.kind === 'validation') {
      const fields: FieldName[] = ['author', 'text'];
      let firstInvalid: FieldName | null = null;
      for (const field of fields) {
        const message = fieldMessage(err.fieldErrors, field);
        if (!message) {
          continue;
        }
        const control = this.form.controls[field];
        control.setErrors({ ...control.errors, server: message });
        control.markAsTouched();
        firstInvalid ??= field;
      }
      this.serverMessage.set('The API rejected this quote. Please fix the highlighted field.');
      if (firstInvalid) {
        this.focusField(firstInvalid);
      }
      return;
    }

    // 'problem' (a domain rule rejected it), 'network' (status 0), and
    // 'unknown' (e.g. a 401 with an empty body) all already carry a
    // ready-to-render message from errorMappingInterceptor - no per-kind
    // handling needed beyond the field-level case above.
    this.serverMessage.set(err.message);
  }

  private errorMessageFor(control: FormControl<string>, label: string, maxLength: number): string | null {
    if (!control.invalid || !(control.touched || control.dirty)) {
      return null;
    }
    const errors = control.errors;
    if (!errors) {
      return null;
    }
    if (typeof errors['server'] === 'string') {
      return errors['server'];
    }
    if (errors['required'] || errors['blank']) {
      return `Enter ${label}.`;
    }
    if (errors['maxlength']) {
      return `Must be ${maxLength} characters or fewer.`;
    }
    return 'This field is invalid.';
  }

  private focusFirstInvalidField(): void {
    if (this.form.controls.author.invalid) {
      this.focusField('author');
    } else if (this.form.controls.text.invalid) {
      this.focusField('text');
    }
  }

  private focusField(field: FieldName): void {
    const ref = field === 'author' ? this.authorInput() : this.textInput();
    ref?.nativeElement.focus();
  }
}
