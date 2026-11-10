/** Move by global indexes, independent of the portion currently visible in the editor. */
export function moveEvent(events, from, to) {
  if (from === to || from < 0 || to < 0 || from >= events.length || to >= events.length) return events;
  const next = events.slice();
  const [event] = next.splice(from, 1);
  next.splice(to, 0, event);
  return next;
}
