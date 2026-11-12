/** @typedef {'keyDown'|'keyUp'} KeyEventType */

/** @typedef {{ eventId: string, type: KeyEventType, key: string, delayBeforeMs: number }} KeyboardEventDto */

/** @typedef {{ version: number, events: KeyboardEventDto[] }} KeyboardSequenceDto */

/**
 * @typedef {Object} NodeListItem
 * @property {string} nodeId
 * @property {string} name
 * @property {string} description
 * @property {string[]} aliasNames
 * @property {string} hotkeyDisplay
 * @property {boolean} isPinned
 * @property {string} dragText
 * @property {boolean} isBuiltIn
 * @property {boolean} isReadOnly
 * @property {boolean} canEdit
 * @property {boolean} canDelete
 */

/**
 * @typedef {Object} NodeWrite
 * @property {string} name
 * @property {string} description
 * @property {number} beforeDelayMs
 * @property {number} afterDelayMs
 * @property {KeyboardSequenceDto} sequence
 */

/**
 * @typedef {NodeWrite & {
 *   nodeId: string,
 *   isBuiltIn: boolean,
 *   isReadOnly: boolean,
 *   canEdit: boolean,
 *   canDelete: boolean
 * }} NodeDetail
 */

/**
 * @typedef {Object} ApiError
 * @property {string} code
 * @property {string} message
 * @property {string|null} [fieldPath]
 * @property {number|null} [eventIndex]
 * @property {string|null} [nodeId]
 * @property {string|null} [runId]
 * @property {Record<string, unknown>|null} [details]
 */

/**
 * @typedef {{ ok: true, data: T, error: null } | { ok: false, data: null, error: ApiError }} ApiResult
 * @template T
 */

export const BUILTIN_COPY_ID = 'E32C3712-9407-4A41-8DD8-134810F5F93D';
export const BUILTIN_CUT_ID = '00C1CF3E-39C4-464F-8D37-A80C47B414F3';
export const BUILTIN_PASTE_ID = 'C13771F3-65A4-4F0B-A2B9-11798D8AFA24';
export const MAX_DELAY_MS = 2147483647;

export function delayErrorMessage(delay, fieldName) {
  if (Number.isInteger(delay) && delay >= 0 && delay <= MAX_DELAY_MS) return null;
  return `${fieldName}必须是 0～${MAX_DELAY_MS} 毫秒之间的整数`;
}

export const EVENT_TYPE_LABELS = {
  keyDown: '按下',
  keyUp: '抬起',
};

export const KEY_DISPLAY = {
  LeftControl: '左 Ctrl',
  RightControl: '右 Ctrl',
  LeftShift: '左 Shift',
  RightShift: '右 Shift',
  LeftAlt: '左 Alt',
  RightAlt: '右 Alt',
  LeftWin: '左 Win',
  RightWin: '右 Win',
  Enter: 'Enter',
  NumpadEnter: '小键盘 Enter',
  Escape: 'Esc',
  Space: '空格',
  Backspace: '退格',
  Tab: 'Tab',
  CapsLock: 'CapsLock',
  ArrowLeft: '←',
  ArrowUp: '↑',
  ArrowRight: '→',
  ArrowDown: '↓',
  KeyC: 'C',
  KeyX: 'X',
  KeyV: 'V',
  KeyA: 'A',
};

export function keyDisplay(key) {
  if (!key) return '';
  if (KEY_DISPLAY[key]) return KEY_DISPLAY[key];
  if (/^Key[A-Z]$/.test(key)) return key.slice(3);
  if (/^Digit\d$/.test(key)) return key.slice(5);
  if (/^F\d{1,2}$/.test(key)) return key;
  if (/^Numpad\d$/.test(key)) return `小键盘 ${key.slice(6)}`;
  return key;
}

/** 按键帽短标签（预览用） */
export function keyCapLabel(key) {
  if (!key) return '';
  const map = {
    LeftControl: 'Ctrl',
    RightControl: 'Ctrl',
    LeftShift: 'Shift',
    RightShift: 'Shift',
    LeftAlt: 'Alt',
    RightAlt: 'Alt',
    LeftWin: 'Win',
    RightWin: 'Win',
    Escape: 'Esc',
    Space: 'Space',
    Backspace: '⌫',
    Enter: 'Enter',
    NumpadEnter: 'Enter',
    Tab: 'Tab',
    CapsLock: 'Caps',
    ArrowLeft: '←',
    ArrowUp: '↑',
    ArrowRight: '→',
    ArrowDown: '↓',
  };
  if (map[key]) return map[key];
  if (/^Key[A-Z]$/.test(key)) return key.slice(3);
  if (/^Digit\d$/.test(key)) return key.slice(5);
  if (/^F\d{1,2}$/.test(key)) return key;
  if (/^Numpad\d$/.test(key)) return key.slice(6);
  return keyDisplay(key);
}

/**
 * 从事件序列推导组合键摘要（取首次抬起前的按下序列），如 Ctrl + A。
 * @param {{ type: string, key: string }[]} events
 */
export function chordSummary(events) {
  if (!Array.isArray(events) || events.length === 0) return '';
  const downs = [];
  for (const ev of events) {
    if (ev.type === 'keyUp') break;
    if (ev.type === 'keyDown') {
      const label = keyCapLabel(ev.key);
      if (label && !downs.includes(label)) downs.push(label);
    }
  }
  return downs.join(' + ');
}

export function newEventId() {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID();
  }
  return `xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx`.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export function emptyDraft(partial = {}) {
  return {
    nodeId: null,
    name: '',
    description: '',
    beforeDelayMs: 0,
    afterDelayMs: 0,
    sequence: { version: 2, events: [] },
    isBuiltIn: false,
    isReadOnly: false,
    canEdit: true,
    canDelete: true,
    ...partial,
  };
}

export function toNodeWrite(draft) {
  return {
    name: draft.name ?? '',
    description: draft.description ?? '',
    beforeDelayMs: Number(draft.beforeDelayMs) || 0,
    afterDelayMs: Number(draft.afterDelayMs) || 0,
    sequence: {
      version: draft.sequence?.version ?? 2,
      events: (draft.sequence?.events ?? []).map((e) => ({
        eventId: e.eventId,
        type: e.type,
        key: e.key,
        delayBeforeMs: Number(e.delayBeforeMs) || 0,
      })),
    },
  };
}
