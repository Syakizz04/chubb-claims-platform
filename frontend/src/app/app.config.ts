import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { Api } from './core/api';
import { Session, devIdentityInterceptor } from './core/session';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([devIdentityInterceptor])),
    // Load the seeded users before routing, so role guards know who is signed in.
    provideAppInitializer(async () => {
      const api = inject(Api);
      const session = inject(Session);
      try {
        session.users.set(await firstValueFrom(api.users()));
      } catch {
        session.users.set([]); // backend down: the welcome page still renders
      }
    }),
  ],
};
