import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { BookDto, BookRequest } from './book.models';

/** Thin HTTP wrapper over the QuestionBank module's /v1/books endpoints. Each change returns the book as it is afterwards. */
@Injectable({ providedIn: 'root' })
export class BookApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/v1/books`;

  list(includeArchived = false): Observable<BookDto[]> {
    return this.http.get<BookDto[]>(this.baseUrl, { params: includeArchived ? { includeArchived: true } : {} });
  }

  get(bookId: string): Observable<BookDto> {
    return this.http.get<BookDto>(`${this.baseUrl}/${bookId}`);
  }

  create(request: BookRequest): Observable<BookDto> {
    return this.http.post<BookDto>(this.baseUrl, request);
  }

  update(bookId: string, request: BookRequest): Observable<BookDto> {
    return this.http.put<BookDto>(`${this.baseUrl}/${bookId}`, request);
  }

  archive(bookId: string): Observable<BookDto> {
    return this.http.post<BookDto>(`${this.baseUrl}/${bookId}/archive`, {});
  }

  restore(bookId: string): Observable<BookDto> {
    return this.http.post<BookDto>(`${this.baseUrl}/${bookId}/restore`, {});
  }

  addChapter(bookId: string, title: string): Observable<BookDto> {
    return this.http.post<BookDto>(`${this.baseUrl}/${bookId}/chapters`, { title });
  }

  renameChapter(bookId: string, chapterId: string, title: string): Observable<BookDto> {
    return this.http.put<BookDto>(`${this.baseUrl}/${bookId}/chapters/${chapterId}`, { title });
  }

  archiveChapter(bookId: string, chapterId: string): Observable<BookDto> {
    return this.http.post<BookDto>(`${this.baseUrl}/${bookId}/chapters/${chapterId}/archive`, {});
  }

  restoreChapter(bookId: string, chapterId: string): Observable<BookDto> {
    return this.http.post<BookDto>(`${this.baseUrl}/${bookId}/chapters/${chapterId}/restore`, {});
  }
}
