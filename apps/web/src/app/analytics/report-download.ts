import { HttpResponse } from '@angular/common/http';
import { FileDownloadService } from '../shared/file-download/file-download.service';

/** The two formats a report is downloaded in (FR-38). */
export type ReportFormat = 'csv' | 'pdf';

/** The file name the server gave the download, read from its Content-Disposition header, or null when the header does not carry one. */
export function fileNameOf(response: HttpResponse<Blob>): string | null {
  const disposition = response.headers.get('content-disposition') ?? '';
  const match = /filename="?([^";]+)"?/i.exec(disposition);
  return match ? match[1] : null;
}

/**
 * Saves a downloaded report under the name the server gave it, or the fallback when the server gave none. Returns false when the response
 * carried no file. The export is a POST, which a plain link cannot make, so the file is saved from memory.
 */
export function saveDownload(response: HttpResponse<Blob>, fallbackName: string, downloads: FileDownloadService): boolean {
  const blob = response.body;
  if (blob === null) {
    return false;
  }

  downloads.save(blob, fileNameOf(response) ?? fallbackName);
  return true;
}
