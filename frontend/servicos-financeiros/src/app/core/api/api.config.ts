/**
 * Base das chamadas à API. É relativa de propósito: em desenvolvimento o `ng serve` encaminha
 * `/api` para o backend (proxy.conf.json) e, no Docker, o nginx faz o mesmo. Assim o front não
 * precisa de CORS nem de URLs por ambiente.
 */
export const API_BASE = '/api';
