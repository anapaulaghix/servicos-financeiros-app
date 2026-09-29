import { isUuid } from '../utils/uuid';
import { IdempotencyKeyTracker } from './idempotency-key-tracker';

describe('IdempotencyKeyTracker', () => {
  const payload = { accountId: 'acc-1', type: 'CREDIT', amount: 10 };
  let tracker: IdempotencyKeyTracker<typeof payload>;

  beforeEach(() => (tracker = new IdempotencyKeyTracker()));

  it('gera uma chave UUID', () => {
    expect(isUuid(tracker.keyFor(payload))).toBeTrue();
  });

  it('reutiliza a chave ao reenviar os mesmos dados sem confirmação (retry seguro)', () => {
    expect(tracker.keyFor({ ...payload })).toBe(tracker.keyFor({ ...payload }));
  });

  it('gera outra chave quando os dados mudam', () => {
    const first = tracker.keyFor(payload);

    expect(tracker.keyFor({ ...payload, amount: 20 })).not.toBe(first);
  });

  it('depois de confirmada, dados iguais viram um novo lançamento', () => {
    const first = tracker.keyFor(payload);
    tracker.complete();

    expect(tracker.keyFor(payload)).not.toBe(first);
  });
});
