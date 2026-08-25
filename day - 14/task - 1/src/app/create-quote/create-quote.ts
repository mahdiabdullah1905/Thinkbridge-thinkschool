import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AUTHOR_MAX_LENGTH,
  CreateQuoteApi,
  CreateQuoteFailure,
  Quote,
  TEXT_MAX_LENGTH,
} from './create-quote-api';
import { notBlankValidator } from './create-quote.validators';

type SubmitStatus = 'idle' | 'submitting' | 'success' | 'error';
type FieldName = 'author' | 'text';

@Component({
  selector: 'app-create-quote',
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote.html',
  styleUrl: './create-quote.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateQuote {
  private readonly api = inject(CreateQuoteApi);

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
  protected readonly createdQuote = signal<Quote | null>(null);

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

    this.api.createQuote({ author, text }).subscribe({
      next: (quote) => {
        this.status.set('success');
        this.createdQuote.set(quote);
        this.form.reset({ author: '', text: '' });
      },
      error: (failure: CreateQuoteFailure) => {
        this.status.set('error');
        this.applyFailure(failure);
      },
    });
  }

  private applyFailure(failure: CreateQuoteFailure): void {
    switch (failure.kind) {
      case 'fieldErrors': {
        const fields: FieldName[] = ['author', 'text'];
        let firstInvalid: FieldName | null = null;
        for (const field of fields) {
          const message = failure.fieldErrors[field];
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
        break;
      }
      case 'unauthorized':
        this.serverMessage.set('You need to be signed in to add a quote. Please sign in and try again.');
        break;
      case 'network':
        this.serverMessage.set('Could not reach the quotes API. Check your connection and try again.');
        break;
      case 'serverMessage':
        this.serverMessage.set(failure.message);
        break;
    }
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
