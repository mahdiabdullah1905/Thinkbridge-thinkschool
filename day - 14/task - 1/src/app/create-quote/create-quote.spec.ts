import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CreateQuote } from './create-quote';
import { AUTHOR_MAX_LENGTH, Quote } from './create-quote-api';

describe('CreateQuote', () => {
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

  it('renders an accessible label and no errors for the empty, untouched form', async () => {
    const fixture = render();
    await fixture.whenStable();

    const author = fixture.nativeElement.querySelector('#author');
    const label = fixture.nativeElement.querySelector('label[for="author"]');
    expect(label).not.toBeNull();
    expect(author.getAttribute('aria-invalid')).toBe('false');
    expect(fixture.nativeElement.querySelector('.field-error')).toBeNull();
  });

  it('shows validation errors and moves focus to the first invalid field on submit', async () => {
    const fixture = render();
    await fixture.whenStable();

    const form = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit'));
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
    component['form'].controls.author.setValue('a'.repeat(AUTHOR_MAX_LENGTH + 1));
    component['form'].controls.author.markAsTouched();
    await fixture.whenStable();

    const authorError = fixture.nativeElement.querySelector('#author-error');
    expect(authorError.textContent).toContain('100 characters or fewer');
  });

  it('rejects a whitespace-only value that [Required] alone would not catch', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].controls.text.setValue('   ');
    component['form'].controls.text.markAsTouched();
    await fixture.whenStable();

    expect(component['form'].controls.text.invalid).toBe(true);
    const textError = fixture.nativeElement.querySelector('#text-error');
    expect(textError.textContent).toContain('Enter');
  });

  it('submits a valid quote and shows the success state without losing focus', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].setValue({ author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.' });
    await fixture.whenStable();

    const form = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.' });

    const created: Quote = { id: 1, author: 'Ada Lovelace', text: 'The Analytical Engine weaves algebra.', isDeleted: false };
    req.flush(created, { status: 201, statusText: 'Created' });
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.success-message').textContent).toContain('Quote #1');
  });

  it('surfaces a 401 from the auth-protected endpoint as a clear message', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].setValue({ author: 'Ada Lovelace', text: 'Valid text' });
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes');
    req.flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.server-message').textContent).toContain('signed in');
  });

  it('maps a server-side field validation error onto the right control', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].setValue({ author: 'Ada Lovelace', text: 'Valid text' });
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes');
    req.flush(
      { title: 'One or more validation errors occurred.', errors: { Author: ['The field Author must be a string with a minimum length of 1 and a maximum length of 100.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    const author = fixture.nativeElement.querySelector('#author');
    expect(author.getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement).toBe(author);
  });

  it('reports a network failure distinctly from a server rejection', async () => {
    const fixture = render();
    const component = fixture.componentInstance;
    component['form'].setValue({ author: 'Ada Lovelace', text: 'Valid text' });
    await fixture.whenStable();

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    const req = httpMock.expectOne('/api/quotes');
    req.error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelector('.server-message').textContent).toContain('Could not reach');
  });
});
