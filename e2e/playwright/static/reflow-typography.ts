import type { Page } from '@playwright/test';

export const textModes = ['normal', 'spacing', 'enlarged', 'combined'] as const;
export type TextMode = typeof textModes[number];

export function applyTextMode(page: Page, mode: TextMode) {
  const enlarged = mode === 'enlarged' || mode === 'combined';
  const spaced = mode === 'spacing' || mode === 'combined';
  return page.addStyleTag({ content: `
    html { font-size: ${enlarged ? 32 : 16}px !important; }
    ${spaced ? '* { line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important; } p { margin-bottom: 2em !important; }' : ''}
  ` });
}
