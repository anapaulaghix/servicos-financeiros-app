import { definePreset } from '@primeng/themes';
import Aura from '@primeng/themes/aura';

/**
 * Tema do produto sobre o preset Aura do PrimeNG: paleta mínima (papel, tinta e um acento vermelho),
 * campos com traço firme e nenhum canto arredondado. Para adaptar a outra marca, ajuste este arquivo
 * e src/styles/_tokens.scss.
 */
const messageTone = (border: string, color: string) => ({
  background: '#fbf9f4',
  borderColor: border,
  color,
  shadow: 'none',
  outlinedBorderColor: border,
  outlinedColor: color,
  simpleText: color,
});

export const AppPreset = definePreset(Aura, {
  components: {
    // Mensagens: fundo de papel e traço firme; só erros e avisos usam o acento vermelho.
    message: {
      colorScheme: {
        light: {
          success: messageTone('#0e0e0e', '#0e0e0e'),
          info: messageTone('#0e0e0e', '#0e0e0e'),
          warn: messageTone('#e8391d', '#b32a12'),
          error: messageTone('#e8391d', '#b32a12'),
        },
      },
    },
    tag: {
      colorScheme: {
        light: {
          danger: { background: '#e8391d', color: '#ffffff' },
          contrast: { background: '#0e0e0e', color: '#f3f0e8' },
        },
      },
    },
    progressbar: {
      root: { background: '#cfc9b8', height: '6px' },
      value: { background: '#0e0e0e' },
    },
  },
  primitive: {
    borderRadius: { none: '0', xs: '0', sm: '0', md: '0', lg: '0', xl: '0' },
  },
  semantic: {
    primary: {
      50: '#f3f0e8',
      100: '#e6e2d6',
      200: '#cfc9b8',
      300: '#b3ad9b',
      400: '#8a8676',
      500: '#55524a',
      600: '#3a3833',
      700: '#26251f',
      800: '#171613',
      900: '#0e0e0e',
      950: '#0e0e0e',
    },
    focusRing: { width: '3px', style: 'solid', color: '#e8391d', offset: '2px' },
    colorScheme: {
      light: {
        surface: {
          0: '#fbf9f4',
          50: '#f3f0e8',
          100: '#e6e2d6',
          200: '#cfc9b8',
          300: '#b3ad9b',
          400: '#8a8676',
          500: '#55524a',
          600: '#3a3833',
          700: '#26251f',
          800: '#171613',
          900: '#0e0e0e',
          950: '#0e0e0e',
        },
        primary: {
          color: '#0e0e0e',
          contrastColor: '#f3f0e8',
          hoverColor: '#e8391d',
          activeColor: '#b32a12',
        },
        highlight: {
          background: '#0e0e0e',
          focusBackground: '#0e0e0e',
          color: '#f3f0e8',
          focusColor: '#f3f0e8',
        },
        formField: {
          borderColor: '#0e0e0e',
          hoverBorderColor: '#0e0e0e',
          focusBorderColor: '#e8391d',
          invalidBorderColor: '#e8391d',
          background: '#fbf9f4',
          color: '#0e0e0e',
        },
      },
    },
  },
});
