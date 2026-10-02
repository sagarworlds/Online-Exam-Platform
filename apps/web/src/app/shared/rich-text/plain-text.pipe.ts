import { Pipe, PipeTransform } from '@angular/core';
import { htmlToPlainText } from './html-to-text';

/** `{{ question.text | plainText }}`: question HTML as one line of text, for dropdown options and list summaries. */
@Pipe({ name: 'plainText' })
export class PlainTextPipe implements PipeTransform {
  transform(html: string | null | undefined, maxLength?: number): string {
    return htmlToPlainText(html, maxLength);
  }
}
