import { Component } from '@angular/core';
import { CreateQuote } from './create-quote/create-quote';

@Component({
  selector: 'app-root',
  imports: [CreateQuote],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
