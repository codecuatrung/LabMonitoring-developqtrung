export interface ScreenSummary {
  id: number;
  code: string;
  name: string;
  /** Null for a submenu group header (e.g. "Hệ thống") — not a page of its own. */
  route: string | null;
  icon: string | null;
  displayOrder: number;
  isActive: boolean;
  /** Null for a top-level sidebar entry; set to nest under a submenu group. */
  parentId: number | null;
}

/** Flattens the screen registry into top-level-then-children display order, indenting children —
 * shared by the sidebar menu, the screens registry table, and role/user screen checklists. */
export function flattenScreensForChecklist(all: ScreenSummary[]): { screen: ScreenSummary; depth: number }[] {
  const byParent = new Map<number | null, ScreenSummary[]>();
  for (const s of all) {
    const key = s.parentId;
    (byParent.get(key) ?? byParent.set(key, []).get(key)!).push(s);
  }
  for (const list of byParent.values()) list.sort((a, b) => a.displayOrder - b.displayOrder);

  const result: { screen: ScreenSummary; depth: number }[] = [];
  for (const top of byParent.get(null) ?? []) {
    result.push({ screen: top, depth: 0 });
    for (const child of byParent.get(top.id) ?? []) result.push({ screen: child, depth: 1 });
  }
  return result;
}
