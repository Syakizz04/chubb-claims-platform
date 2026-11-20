import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, Injectable, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { Api } from './core/api';
import { Session, devIdentityInterceptor } from './core/session';

/** "My claims · Lantern" in the browser tab; the welcome route's title is already complete. */
@Injectable()
class LanternTitle extends TitleStrategy {
  private title = inject(Title);
  override updateTitle(state: RouterStateSnapshot) {
    const t = this.buildTitle(state);
    this.title.setTitle(!t ? 'Lantern' : t.startsWith('Lantern') ? t : `${t} · Lantern`);
  }
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useClass: LanternTitle },
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
