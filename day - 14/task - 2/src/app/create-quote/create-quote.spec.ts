import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CreateQuote } from './create-quote';
import { AUTHOR_MAX_LENGTH, TEXT_MAX_LENGTH, Quote } from './create-quote-api';

// [formRoot]'s host (submit) listener calls submit() on the field tree without awaiting
// or otherwise exposing the returned promise (confirmed in the compiled
// @angular/forms/signals bundle: `onSubmit(event): void`, not Promise<void>). That's fine
// for real usage, but it means fixture.whenStable() alone is not guaranteed to wait for an
// async submission action's HTTP round trip in tests - an extra macrotask tick is needed
// after resolving the mocked HTTP request. See README "Mistake caught" section.
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('CreateQuote (Signal Forms)', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CreateQuote],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function render() {
    const fixture = TestBed.createComponent(CreateQuote);
    fixture.autoDetectChanges();
    return fixture;
  }

  it('renders an accessible label and no errors for the pristine, untouched form', async () => {
    const fixture = render();
    await fixture.whenStable();

    const author = fixture.nativeElement.querySelector('#author');
    const label = fixture.nativeElement.querySelector('label[for="author"]');
    expect(label).not.toBeNull();
    expect(author.getAttribute('aria-invalid')).toBe('false');
    expect(fixture.nativeElement.querySelector('.field-error')).toBeNull();
    // required() auto-binds the native `required` DOM property - free, no [required] in the template.
    expect(author.required).toBe(true);
    // maxLength() auto-binds the native `maxLength` DOM property from a single schema call.
    expect(author.maxLength).toBe(AUTHOR_MAX_LENGTH);
  });

  it('is dirty/touched only after the user actually interacts with a field', async () => {
    // WRONG ASSUMPTION CHECK: calling `field().value.set(...)` directly does NOT mark the
    // field dirty - only a real DOM (input) event does (confirmed in the compiled bundle's
    // nativeControlCreate: dirty/touched are wired to DOM 'input'/'blur' listeners, not to
    // the value signal itself). This mirrors how classic reactive forms' setValue() doesn't
    // mark dirty either, so it isn't a Signal Forms-specific regression - but an early draft
    // of this test asserted dirty() right after a raw .value.set() and failed, which is what
    // caught it. Driving the real <input> here instead of the signal is the fix.
    const fixture = render();
    const component = fixture.componentInstance;
    await fixture.whenStable();

    expect(component['quoteForm'].author().dirty()).toBe(false);
    expect(component['quoteForm'].author().touched()).toBe(false);

    const author: HTMLInputElement = fixture.nativeElement.querySelector('#author');
    author.value = 'Ada';
    author.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(component['quoteForm'].author().dirty()).toBe(true);
    expect(component['quoteForm'].author().touched()).toBe(false);

    author.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(component['quoteForm'].author().touched()).toBe(true);
  });

  it('shows validation errors and moves focus to the first invalid field on submit', async () => {
    const fixture = render();
    await fixture.whenStable();

    const form = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    const author = fixture.nativeElement.querySelector('#author');
    const authorError = fixture.nativeElement.querySelector('#author-error');
    expect(author.getAttribute('aria-invalid')).toBe('true');
    expect(author.getAttribute('aria-describedby')).toBe('author-error');
    expect(authorError.textContent).toContain('Enter');
    expect(document.activeElement).toBe(author);

    httpMock.expectNone('/api/quotes');
  });

  it('rejects an author over the API limit of 100 characters', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['quoteForm'].author().value.set('a'.repeat(AUTHOR_MAX_LENGTH + 1));
    component['quoteForm'].author().markAsTouched();
    await fixture.whenStable();

    const authorError = fixture.nativeElement.querySelector('#author-error');
    expect(authorError.textContent).toContain('100 characters or fewer');
  });

  it('rejects a text over the API limit of 1000 characters', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['quoteForm'].text().value.set('a'.repeat(TEXT_MAX_LENGTH + 1));
    component['quoteForm'].text().markAsTouched();
    await fixture.whenStable();

    const textError = fixture.nativeElement.querySelector('#text-error');
    expect(textError.textContent).toContain('1000 characters or fewer');
    expect(fixture.nativeElement.querySelector('#text').getAttribute('aria-invalid')).toBe('true');
  });

  it('WRONG ASSUMPTION CHECK: required() alone does not catch a whitespace-only value', async () => {
    const fixture = render();
    const component = fixture.componentInstance;

    component['quoteForm'].text().value.set('   ');
    await fixture.whenStable();

    // required()'s own error must NOT be present - isEmpty() in the compiled bundle only
    // checks `=== ''`, so a 3-space string satisfies it. If this assertion ever fails, the
    // preview API's required() started trimming and the separate validate() rule below
    // (and the identical comment in create-quote.ts) would be stale and should be removed.
    const kinds = component['quoteForm'].text().errors().map((e: { kind: string }) => e.kind);
    expect(kinds).not.toContain('required');
    // Our own validate() rule is what actually catches it.
    expect(kinds).toContain('blank');
  });

  it('rejects a whitespace-only value via the custom blank validator', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['quoteForm'].text().value.set('   ');
    component['quoteForm'].text().markAsTouched();
    await fixture.whenStable();

    expect(component['quoteForm'].text().invalid()).toBe(true);
    const textError = fixture.nativeElement.querySelector('#text-error');
    expect(textError.textContent).toContain('Enter');
  });

  it('submits a valid quote and shows the success state without losing focus', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['quoteForm'].author().value.set('Ada Lovelace');
    component['quoteForm'].text().value.set('The Analytical Engine weaves algebra.');
    await fixture.whenStable();

    const form = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.' });

    const created: Quote = {
      id: 1,
      author: 'Ada Lovelace',
      text: 'The Analytical Engine weaves algebra.',
      isDeleted: false,
    };
    req.flush(created, { status: 201, statusText: 'Created' });
    await fixture.whenStable();
    await flushMicrotasks();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.success-message').textContent).toContain('Quote #1');
    // form value is reset after a successful submit
    expect(component['quoteForm']().value()).toEqual({ author: '', text: '' });
  });

  it('surfaces a 401 from the auth-protected endpoint as a clear root-level message', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['quoteForm'].author().value.set('Ada Lovelace');
    component['quoteForm'].text().value.set('Valid text');
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes');
    req.flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    await flushMicrotasks();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.server-message').textContent).toContain('signed in');
  });

  it('maps a server-side field validation error onto the right field via fieldTree targeting', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['quoteForm'].author().value.set('Ada Lovelace');
    component['quoteForm'].text().value.set('Valid text');
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes');
    req.flush(
      {
        title: 'One or more validation errors occurred.',
        errors: { Author: ['The field Author must be a string with a minimum length of 1 and a maximum length of 100.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    await flushMicrotasks();
    await fixture.whenStable();

    const author = fixture.nativeElement.querySelector('#author');
    expect(author.getAttribute('aria-invalid')).toBe('true');
    expect(component['quoteForm'].author().errors()[0]?.kind).toBe('server');
    // text field must NOT have been touched by the author-targeted error
    expect(component['quoteForm'].text().invalid()).toBe(false);
  });

  it('clears a server-set field error automatically once that field is edited again', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['quoteForm'].author().value.set('Ada Lovelace');
    component['quoteForm'].text().value.set('Valid text');
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    httpMock
      .expectOne('/api/quotes')
      .flush({ errors: { Author: ['Server says no.'] } }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    await flushMicrotasks();
    await fixture.whenStable();

    expect(component['quoteForm'].author().invalid()).toBe(true);

    component['quoteForm'].author().value.set('Ada Lovelace II');
    await fixture.whenStable();

    expect(component['quoteForm'].author().errors().some((e: { kind: string }) => e.kind === 'server')).toBe(false);
  });

  it('reports a network failure distinctly from a server rejection', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['quoteForm'].author().value.set('Ada Lovelace');
    component['quoteForm'].text().value.set('Valid text');
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes');
    req.error(new ProgressEvent('error'));
    await fixture.whenStable();
    await flushMicrotasks();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.server-message').textContent).toContain('Could not reach');
  });
});
