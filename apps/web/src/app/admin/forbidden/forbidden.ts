import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Shown to a signed-in user who opened a page their role does not allow. */
@Component({
  selector: 'app-forbidden',
  imports: [RouterLink],
  templateUrl: './forbidden.html',
})
export class Forbidden {}
