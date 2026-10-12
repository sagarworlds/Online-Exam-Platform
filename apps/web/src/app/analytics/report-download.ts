import { HttpResponse } from '@angular/common/http';

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
 * carried no file. The file is saved through a temporary address and a link click because the export is a POST, which a plain link cannot
 * make.
 */
export function saveDownload(
  response: HttpResponse<Blob>,
  fallbackName: string,
  document: Document,
): boolean {
  const blob = response.body;
  if (blob === null) {
    return false;
  }

  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileNameOf(response) ?? fallbackName;
  link.click();
  // The download starts with the click; the address can be released once the browser has had a moment to read it.
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  return true;
}
