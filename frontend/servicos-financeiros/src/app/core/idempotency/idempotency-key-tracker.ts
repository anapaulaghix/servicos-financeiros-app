import { newUuid } from '../utils/uuid';

/**
 * Gera a chave de idempotência (eventId) sem expô-la ao usuário.
 *
 * A mesma chave é reutilizada enquanto o usuário reenvia exatamente os mesmos dados de uma tentativa
 * que não teve confirmação (ex.: a rede caiu). Assim, se a primeira tentativa chegou ao servidor, a
 * segunda é reconhecida como duplicada e nada é lançado duas vezes. Qualquer alteração nos dados, ou
 * a confirmação da tentativa (`complete`), faz a próxima operação receber uma chave nova.
 */
export class IdempotencyKeyTracker<T> {
  private pending: { key: string; fingerprint: string } | null = null;

  keyFor(payload: T): string {
    const fingerprint = JSON.stringify(payload);
    if (this.pending?.fingerprint !== fingerprint) {
      this.pending = { key: newUuid(), fingerprint };
    }
    return this.pending.key;
  }

  complete(): void {
    this.pending = null;
  }
}
