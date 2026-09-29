import { isUuid, newUuid } from './uuid';

describe('uuid', () => {
  it('gera identificadores válidos e diferentes entre si', () => {
    const first = newUuid();
    const second = newUuid();

    expect(isUuid(first)).toBeTrue();
    expect(first).not.toBe(second);
  });

  it('gera UUID v4 mesmo sem crypto.randomUUID (contexto HTTP inseguro)', () => {
    const original = crypto.randomUUID;
    Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true });

    try {
      const id = newUuid();
      expect(isUuid(id)).toBeTrue();
      expect(id[14]).toBe('4');
    } finally {
      Object.defineProperty(crypto, 'randomUUID', { value: original, configurable: true });
    }
  });

  it('rejeita textos que não são UUID', () => {
    expect(isUuid('')).toBeFalse();
    expect(isUuid('nao-e-uuid')).toBeFalse();
    expect(isUuid('3fa85f64-5717-4562-b3fc-2c963f66afa')).toBeFalse();
  });
});
