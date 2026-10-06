import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { deviceSignature } from './device-signature';

/** The request header the API reads the device signature from. */
export const DEVICE_SIGNATURE_HEADER = 'X-Device-Fingerprint';

/**
 * Sends the device signature (FR-26) with every request to the platform's own API, so a sign-in and the attempt that follows it name
 * the same device. Requests to anywhere else are left alone: the signature is for the platform, not for third parties.
 */
export const deviceSignatureInterceptor: HttpInterceptorFn = (request, next) =>
  request.url.startsWith(environment.apiBaseUrl) && !request.headers.has(DEVICE_SIGNATURE_HEADER)
    ? next(request.clone({ setHeaders: { [DEVICE_SIGNATURE_HEADER]: deviceSignature() } }))
    : next(request);
