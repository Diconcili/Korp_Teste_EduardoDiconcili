import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { tap } from 'rxjs';
import { LoginChallenge, SessionResult } from './models';

@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly api = 'http://localhost:5102/api';
  token = '';

  constructor(private http: HttpClient) {}

  login(username: string, password: string) {
    return this.http.post<LoginChallenge>(`${this.api}/auth/login`, {
      username,
      password,
    });
  }
  validateMfa(challenge: string, code: string) {
    return this.http
      .post<SessionResult>(`${this.api}/auth/mfa`, { challenge, code })
      .pipe(tap((session) => this.save(session)));
  }
  logout() {
    return this.http.delete(`${this.api}/auth/session`, {
      headers: this.headers(),
    });
  }
  headers() {
    return new HttpHeaders({ Authorization: `Bearer ${this.token}` });
  }
  clear() {
    sessionStorage.removeItem('korp.session');
    this.token = '';
  }

  restore() {
    const saved = sessionStorage.getItem('korp.session');
    if (!saved) return false;
    try {
      const session = JSON.parse(saved);
      if (
        typeof session.token === 'string' &&
        session.token &&
        session.expiresAt &&
        new Date(session.expiresAt) > new Date()
      ) {
        this.token = session.token;
        return true;
      }
    } catch {
      /* Sessões corrompidas são descartadas localmente. */
    }
    this.clear();
    return false;
  }

  private save(session: SessionResult) {
    this.token = session.token;
    sessionStorage.setItem('korp.session', JSON.stringify(session));
  }
}
