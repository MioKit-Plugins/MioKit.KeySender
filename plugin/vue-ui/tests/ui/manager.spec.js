import { expect, test } from 'playwright/test';

const button = (container, name) => container.getByRole('button', { name, exact: true });
const nodeRow = (page, name) => page.getByRole('row').filter({ has: page.getByRole('cell', { name }) });
const scope = (page, name) => page.getByRole('complementary').getByRole('button', { name });
const editor = (page) => page.getByRole('dialog').filter({ has: page.getByRole('tab', { name: '详细' }) });
const notice = (page, text) => page.locator('[data-sonner-toast]').filter({ hasText: text });
const verticalBounds = (locator) => locator.evaluate((element) => {
  const { top, height } = element.getBoundingClientRect();
  return { top, height };
});

async function openNode(page, name) {
  await nodeRow(page, name).getByText(name, { exact: true }).click();
  await expect(editor(page).getByLabel('名称')).toHaveValue(name);
}

// This is setup for the in-memory mock. All assertions below use the rendered UI.
async function removeQueuedMockRun(page) {
  await page.evaluate(async () => {
    const { getKeySenderApi, withSession } = await import('/src/api/key-sender.js');
    const api = getKeySenderApi();
    const { data } = await api.GetRunsAsync(withSession());
    for (const { runId } of data.active) await api.CancelRunAsync(withSession({ runId }));
  });
}

test.beforeEach(async ({ page }) => {
  await page.goto('/');
  await expect(nodeRow(page, '交错 A/B')).toBeVisible();
  await expect(page.getByText('Mock', { exact: true })).toBeVisible();
});

test('scope and search filter the rows, clear a selection, and show the empty state', async ({ page }) => {
  await expect(page.getByRole('columnheader')).toHaveText(['', '节点', '标记', '操作']);
  await expect(nodeRow(page, '交错 A/B').getByRole('cell')).toHaveCount(4);
  const search = page.getByPlaceholder(/^在「.*」中搜索/);
  await nodeRow(page, '交错 A/B').getByRole('checkbox').check();
  await expect(page.getByText(/已选\s*1/)).toBeVisible();
  await search.fill('  shift down  ');
  await search.press('Enter');
  await expect(nodeRow(page, '按住左 Shift')).toBeVisible();
  await expect(nodeRow(page, '交错 A/B')).toHaveCount(0);
  await expect(page.getByText(/已选\s*1/)).toHaveCount(0);

  await search.fill('不存在的序列');
  await expect(page.getByText('没有匹配的节点')).toBeVisible();
  await button(page, '清空搜索').click();
  await expect(search).toBeEmpty();
  await expect(nodeRow(page, '交错 A/B')).toBeVisible();

  await scope(page, '办公').click();
  await expect(nodeRow(page, '交错 A/B')).toBeVisible();
  await expect(nodeRow(page, '按住左 Shift')).toHaveCount(0);
  await scope(page, '系统内置').click();
  await expect(nodeRow(page, '粘贴')).toBeVisible();
  await expect(nodeRow(page, '交错 A/B')).toHaveCount(0);
  await expect(button(page, '新建')).toBeDisabled();
});

test('a built-in sequence is read-only and copying it creates an independent editable node', async ({ page }) => {
  await scope(page, '系统内置').click();
  await openNode(page, '粘贴');
  const sheet = editor(page);
  await expect(sheet.getByText('内置 · 只读')).toBeVisible();
  await expect(sheet.getByLabel('名称')).toBeDisabled();
  await expect(button(sheet, '开始录制')).toBeDisabled();
  await sheet.getByRole('tab', { name: '详细' }).click();
  await expect(sheet.getByLabel('节点前等待')).toBeDisabled();
  await expect(button(sheet, '删除行').first()).toBeDisabled();

  await button(sheet, '复制为自定义').click();
  await expect(sheet.getByLabel('名称')).toBeEnabled();
  await expect(sheet.getByLabel('名称')).toHaveValue(/粘贴.+/);
  await expect(sheet.getByText('4 条事件')).toBeVisible();
  await sheet.getByLabel('名称').fill('独立粘贴序列');
  await button(sheet, '保存').click();
  await expect(button(sheet, '保存')).toBeDisabled();
  await button(sheet, 'Close').click();
  await scope(page, '全部').click();
  await expect(nodeRow(page, '独立粘贴序列')).toBeVisible();
  await expect(nodeRow(page, '粘贴').filter({ hasText: '内置 · 只读' })).toBeVisible();
});

test('closing a dirty editor supports continue, discard, and save', async ({ page }) => {
  await openNode(page, '交错 A/B');
  const sheet = editor(page);
  const name = sheet.getByLabel('名称');
  const prompt = page.getByRole('dialog', { name: '有未保存的修改' });
  await name.fill('放弃后不会保存');
  await expect(sheet.getByText('未保存')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(prompt).toBeVisible();
  await button(prompt, '继续编辑').click();
  await expect(name).toHaveValue('放弃后不会保存');
  await button(sheet, 'Close').click();
  await button(prompt, '放弃修改').click();
  await expect(sheet).toBeHidden();
  await openNode(page, '交错 A/B');

  await name.fill('保存后关闭');
  await button(sheet, 'Close').click();
  await button(prompt, '保存并继续').click();
  await expect(sheet).toBeHidden();
  await expect(nodeRow(page, '交错 A/B')).toHaveCount(0);
  await openNode(page, '保存后关闭');
  await expect(button(sheet, '保存')).toBeDisabled();
});

test('recording blocks closing, replaces the draft when stopped, and leaves it intact when cancelled', async ({ page }) => {
  await openNode(page, '按住左 Shift');
  const sheet = editor(page);
  await expect(sheet.getByText('1 条事件')).toBeVisible();
  await button(sheet, '开始录制').click();
  await expect(notice(page, '有任务进行中，无法开始录制')).toBeVisible();
  await removeQueuedMockRun(page);
  await button(sheet, '开始录制').click();
  await expect(sheet.getByText('录制中', { exact: true })).toBeVisible();
  await expect(button(sheet, '保存')).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(sheet).toBeVisible();
  await button(sheet, '↓ A').click();
  await button(sheet, '↓ A').click();
  await button(sheet, '↑ A').click();
  await expect(sheet.getByText('2 条', { exact: true })).toBeVisible();
  await button(sheet, '停止录制').click();
  await expect(sheet.getByText('空闲')).toBeVisible();
  await expect(sheet.getByText('2 条事件')).toBeVisible();
  await expect(sheet.getByTitle('按下', { exact: true })).toHaveCount(1);
  await expect(sheet.getByTitle('抬起', { exact: true })).toHaveCount(1);

  await button(sheet, '开始录制').click();
  await button(sheet, '↓ Shift').click();
  await button(sheet, '取消录制').click();
  await expect(sheet.getByText('空闲')).toBeVisible();
  await expect(sheet.getByText('2 条事件')).toBeVisible();
  await button(sheet, '开始录制').click();
  await button(sheet, '停止录制').click();
  await expect(notice(page, '未录到按键')).toBeVisible();
  await expect(sheet.getByText('2 条事件')).toBeVisible();

  await button(sheet, '保存').click();
  await expect(button(sheet, '保存')).toBeDisabled();
  await button(sheet, 'Close').click();
  await openNode(page, '按住左 Shift');
  await expect(sheet.getByText('2 条事件')).toBeVisible();
});

test('save reports blank and overlong names without moving the editor fields', async ({ page }) => {
  await openNode(page, '按住左 Shift');
  const sheet = editor(page);
  const name = sheet.getByLabel('名称');
  const save = button(sheet, '保存');
  const layout = () => Promise.all([name, sheet.getByLabel('描述')].map(verticalBounds));
  const originalLayout = await layout();
  await expect(sheet.getByText(/提示：.*未释放/)).toHaveCount(0);

  await name.fill('');
  await expect(name).toHaveValue('');
  await expect(notice(page, '名称不能为空')).toHaveCount(0);
  await expect(sheet.getByText('名称不能为空')).toHaveCount(0);
  expect(await layout()).toEqual(originalLayout);

  await save.click();
  await expect(notice(page, '名称不能为空')).toBeVisible();
  await expect(sheet.getByText('名称不能为空')).toHaveCount(0);
  await expect(name).toHaveValue('');
  expect(await layout()).toEqual(originalLayout);

  const longName = '长'.repeat(101);
  await name.fill(longName);
  await expect(name).toHaveValue(longName);
  await expect(notice(page, '名称不能超过 100 个字符')).toHaveCount(0);
  await save.click();
  await expect(notice(page, '名称不能超过 100 个字符')).toBeVisible();
  expect(await layout()).toEqual(originalLayout);

  await name.fill('保存时校验名称');
  await save.click();
  await expect(save).toBeDisabled();
  await button(sheet, 'Close').click();
  await expect(nodeRow(page, '保存时校验名称')).toBeVisible();
});

test('event editing validates on save, allows reordering, and persists the selected key and delay', async ({ page }) => {
  await openNode(page, '交错 A/B');
  const sheet = editor(page);
  await sheet.getByRole('tab', { name: '详细' }).click();
  const eventList = sheet.getByRole('list', { name: '按键事件' });
  const events = eventList.getByRole('listitem');
  const first = events.nth(0);
  const second = events.nth(1);
  await expect(events).toHaveCount(4);

  const delay = first.getByRole('spinbutton');
  const delayError = /等待.*0.*2147483647/;
  const layout = () => Promise.all([first, second, delay].map(verticalBounds));
  const originalLayout = await layout();
  await delay.fill('-1');
  await expect(delay).not.toHaveAttribute('aria-invalid', 'true');
  await expect(notice(page, delayError)).toHaveCount(0);
  await expect(sheet.getByText(delayError)).toHaveCount(0);
  expect(await layout()).toEqual(originalLayout);
  await button(sheet, '保存').click();
  await expect(notice(page, delayError)).toBeVisible();
  await expect(sheet.getByText(delayError)).toHaveCount(0);
  await expect(sheet.getByText('未保存')).toBeVisible();
  expect(await layout()).toEqual(originalLayout);
  await delay.fill('225');

  await first.getByRole('combobox', { name: 'A', exact: true }).click();
  await page.getByRole('option', { name: 'C', exact: true }).click();
  await expect(first.getByRole('combobox', { name: 'C', exact: true })).toBeVisible();
  await button(first, '复制行').click();
  await expect(events).toHaveCount(5);
  await expect(second.getByRole('spinbutton')).toHaveValue('225');
  await button(second, '删除行').click();
  await expect(events).toHaveCount(4);

  const source = await button(first, '拖动排序').boundingBox();
  const target = await button(second, '拖动排序').boundingBox();
  await page.mouse.move(source.x + source.width / 2, source.y + source.height / 2);
  await page.mouse.down();
  await page.mouse.move(target.x + target.width / 2, target.y + target.height / 2 + 25, { steps: 8 });
  await page.mouse.up();
  await expect(first.getByRole('spinbutton')).toHaveValue('40');
  await expect(second.getByRole('spinbutton')).toHaveValue('225');
  await sheet.getByLabel('节点前等待').fill('1000');
  await button(sheet, '保存').click();
  await expect(button(sheet, '保存')).toBeDisabled();
  await button(sheet, 'Close').click();
  await openNode(page, '交错 A/B');
  await sheet.getByRole('tab', { name: '详细' }).click();
  await expect(sheet.getByLabel('节点前等待')).toHaveValue('1000');
  await expect(second.getByRole('combobox', { name: 'C', exact: true })).toBeVisible();
  await expect(second.getByRole('spinbutton')).toHaveValue('225');
});

test('category creation, rename, drag and delete update the node list', async ({ page }) => {
  await button(page, '新建分类').click();
  const create = page.getByRole('dialog', { name: '新建分类' });
  await create.getByLabel('名称').fill('  临时分类  ');
  await create.getByLabel('说明').fill('浏览器测试');
  await create.getByLabel('名称').press('Enter');
  await expect(page.getByText('还没有按键序列')).toBeVisible();
  await expect(scope(page, /临时分类.*浏览器测试/)).toBeVisible();

  await button(page, '重命名分类').click();
  const rename = page.getByRole('dialog', { name: '重命名分类' });
  await expect(rename.getByLabel('名称')).toHaveValue('临时分类');
  await rename.getByLabel('名称').fill('已重命名分类');
  await button(rename, '保存').click();
  const category = scope(page, '已重命名分类');
  await expect(category).toBeVisible();

  await scope(page, '全部').click();
  await nodeRow(page, '交错 A/B').dragTo(category);
  await expect(notice(page, '已移动 1 个节点')).toBeVisible();
  await page.keyboard.down('Control');
  try {
    await nodeRow(page, '粘贴').dragTo(category);
  } finally {
    await page.keyboard.up('Control');
  }
  await expect(notice(page, '已复制 1 个节点到分类')).toBeVisible();
  await category.click();
  await expect(nodeRow(page, '交错 A/B')).toBeVisible();
  await expect(nodeRow(page, '粘贴 副本')).toBeVisible();
  await expect(nodeRow(page, '按住左 Shift')).toHaveCount(0);

  await button(page, '删除分类').click();
  const deletion = page.getByRole('dialog', { name: '删除分类？' });
  await expect(deletion).toContainText(/其下全部\s*2 个节点/);
  await button(deletion, '取消').click();
  await expect(category).toBeVisible();
  await button(page, '删除分类').click();
  await button(deletion, '删除分类与节点').click();
  await expect(category).toHaveCount(0);
  await expect(nodeRow(page, '交错 A/B')).toHaveCount(0);
  await expect(nodeRow(page, '按住左 Shift')).toBeVisible();
  await expect(nodeRow(page, '粘贴').filter({ hasText: '内置 · 只读' })).toBeVisible();
});

test('import preview shows the category and commit restores it, while export reports the selected count', async ({ page }) => {
  const preview = page.getByRole('dialog', { name: '导入预览' });
  const importedCategory = scope(page, '导入示例分类');
  await button(page, '导出').click();
  await expect(notice(page, '请先选择要导出的节点')).toBeVisible();
  await button(page, '导入').click();
  await expect(preview.getByText('左 Ctrl 未释放')).toBeVisible();
  await expect(preview.getByText(/分类：导入示例分类/)).toBeVisible();
  await button(preview, '取消').click();
  await expect(preview).toBeHidden();
  await expect(nodeRow(page, '导入示例 · Down-only')).toHaveCount(0);
  await expect(importedCategory).toHaveCount(0);

  await button(page, '导入').click();
  await button(preview, '创建副本').click();
  await expect(preview).toBeHidden();
  const first = nodeRow(page, '导入示例 · Down-only');
  const second = nodeRow(page, '导入示例 · 平衡');
  await expect(first).toBeVisible();
  await expect(second).toBeVisible();
  await first.getByRole('checkbox').check();
  await second.getByRole('checkbox').check();
  await button(page, '导出').click();
  await expect(notice(page, '已导出 2 项')).toContainText('keysender-export-mock.json');
  await expect(importedCategory).toContainText('分类说明示例');
  await importedCategory.click();
  await expect(first).toBeVisible();
  await expect(second).toHaveCount(0);

  await button(page, '导入').click();
  await button(preview, '创建副本').click();
  await expect(preview).toBeHidden();
  await expect(importedCategory).toHaveCount(1);
  await expect(first).toHaveCount(2);
});

test('a partial import closes the consumed preview and shows the saved category', async ({ page }) => {
  await page.evaluate(async () => {
    const { getKeySenderApi, withSession } = await import('/src/api/key-sender.js');
    const api = getKeySenderApi();
    api.CommitImportAsync = async () => {
      const saved = await api.CreateCategoryAsync(withSession({ name: '已保存的分类' }));
      return {
        ok: false,
        error: {
          code: 'persistence_failed',
          message: '保存节点失败',
          details: { persisted: true, createdIds: [], createdCategoryIds: [saved.data.categoryId] },
        },
      };
    };
  });

  const preview = page.getByRole('dialog', { name: '导入预览' });
  await button(page, '导入').click();
  await button(preview, '创建副本').click();
  await expect(preview).toBeHidden();
  await expect(notice(page, '导入未完成，已保存部分内容')).toContainText('保存节点失败');
  await expect(scope(page, '已保存的分类')).toBeVisible();
  await expect(nodeRow(page, '导入示例 · Down-only')).toHaveCount(0);
});

test('a new recording can be saved in the selected category and deleted after confirmation', async ({ page }) => {
  await scope(page, '办公').click();
  await button(page, '新建').click();
  const sheet = editor(page);
  await expect(sheet.getByLabel('名称')).toHaveValue('新节点');
  await sheet.getByLabel('名称').fill('在办公分类中创建');
  await button(sheet, '保存').click();
  const emptySequence = /空序列不能保存|至少.*按键事件/;
  await expect(notice(page, emptySequence)).toBeVisible();
  await expect(sheet.getByText(emptySequence)).toHaveCount(0);
  await removeQueuedMockRun(page);
  await button(sheet, '开始录制').click();
  await button(sheet, '↓ Shift').click();
  await button(sheet, '停止录制').click();
  await expect(sheet.getByText(/提示：.*未释放/)).toHaveCount(0);
  await button(sheet, '保存').click();
  await expect(button(sheet, '保存')).toBeDisabled();
  await button(sheet, 'Close').click();
  const created = nodeRow(page, '在办公分类中创建');
  await expect(created).toBeVisible();
  await expect(nodeRow(page, '按住左 Shift')).toHaveCount(0);
  await openNode(page, '在办公分类中创建');
  await button(sheet, '删除').click();
  const deletion = page.getByRole('dialog', { name: '删除节点' });
  await expect(deletion).toContainText('将删除 1 个节点');
  await button(deletion, '取消').click();
  await expect(sheet).toBeVisible();
  await button(sheet, '删除').click();
  await button(deletion, '确认删除').click();
  await expect(sheet).toBeHidden();
  await expect(created).toHaveCount(0);
  await expect(nodeRow(page, '交错 A/B')).toBeVisible();
});

test('pagination clears page selection and prevents batch deletion of built-in nodes', async ({ page }) => {
  await page.evaluate(async () => {
    const { getKeySenderApi, withSession } = await import('/src/api/key-sender.js');
    for (let i = 1; i <= 16; i++) {
      const result = await getKeySenderApi().CreateNodeAsync(withSession({
        detail: {
          name: `分页测试 ${i}`,
          description: 'pagination fixture',
          sequence: {
            version: 2,
            events: [{ eventId: crypto.randomUUID(), type: 'keyDown', key: 'KeyA', delayBeforeMs: 0 }],
          },
        },
      }));
      if (!result.ok) throw new Error(result.error.message);
    }
  });
  const selectAll = page.getByRole('checkbox', { name: '全选本页' });
  const previous = page.getByRole('button').filter({ has: page.locator('svg.lucide-chevron-left') });
  const next = page.getByRole('button').filter({ has: page.locator('svg.lucide-chevron-right') });
  await expect(page.getByText('1 / 2', { exact: true })).toBeVisible();
  await expect(previous).toBeDisabled();
  await selectAll.check();
  await expect(page.getByText(/已选\s*20/)).toBeVisible();
  await expect(button(page, '选择中包含内置节点，不能批量删除')).toBeDisabled();
  await next.click();
  await expect(page.getByText('2 / 2', { exact: true })).toBeVisible();
  await expect(next).toBeDisabled();
  await expect(selectAll).not.toBeChecked();
  await expect(page.getByRole('table').getByRole('row')).toHaveCount(2);
  await previous.click();
  await expect(page.getByText('1 / 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('table').getByRole('row')).toHaveCount(21);
});

test('hard import diagnostics block creation and a new preview can still succeed', async ({ page }) => {
  await page.evaluate(async () => {
    const { getKeySenderApi } = await import('/src/api/key-sender.js');
    const api = getKeySenderApi();
    const preview = api.PreviewImportAsync;
    api.PreviewImportAsync = async (request) => {
      api.PreviewImportAsync = preview;
      const result = await preview(request);
      result.data.items[0].errors = [{ message: '不支持的按键' }];
      return result;
    };
  });
  const preview = page.getByRole('dialog', { name: '导入预览' });
  await button(page, '导入').click();
  await expect(preview.getByText('不支持的按键')).toBeVisible();
  await expect(button(preview, '创建副本')).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(preview).toBeHidden();
  await expect(nodeRow(page, '导入示例 · Down-only')).toHaveCount(0);
  await button(page, '导入').click();
  await expect(preview.getByText('不支持的按键')).toHaveCount(0);
  await expect(button(preview, '创建副本')).toBeEnabled();
});

test('a 10000-event draft renders visible rows only and saves an edit to the final row', async ({ page }) => {
  const name = '一万条按键事件';
  await page.evaluate(async (nodeName) => {
    const { getKeySenderApi, withSession } = await import('/src/api/key-sender.js');
    const events = Array.from({ length: 10000 }, (_, i) => ({
      eventId: crypto.randomUUID(),
      type: i % 2 ? 'keyUp' : 'keyDown',
      key: 'KeyA',
      delayBeforeMs: i,
    }));
    const result = await getKeySenderApi().CreateNodeAsync(withSession({
      detail: { name: nodeName, sequence: { version: 2, events } },
    }));
    if (!result.ok) throw new Error(result.error.message);
  }, name);
  await openNode(page, name);
  const sheet = editor(page);
  await expect(sheet.getByText('10000 条事件', { exact: true })).toBeVisible();
  await expect(sheet.getByText(/已显示前 60 条，共 10000 条事件/)).toBeVisible();
  const detail = sheet.getByRole('tab', { name: '详细' });
  await detail.click();
  const list = sheet.getByRole('list', { name: '按键事件' });
  const first = list.locator('[aria-posinset="1"]');
  const last = list.locator('[aria-posinset="10000"]');
  const scrollToLast = () => sheet.locator('[data-event-viewport]').evaluate((element) => {
    element.scrollTop = element.scrollHeight;
  });
  await expect(first).toBeVisible();
  await expect(last).toHaveCount(0);
  expect(await list.getByRole('listitem').count()).toBeLessThan(30);
  await scrollToLast();
  await expect(first).toHaveCount(0);
  await expect(last.getByRole('spinbutton')).toHaveValue('9999');
  await last.getByRole('spinbutton').fill('12345');
  await button(sheet, '保存').click();
  await expect(button(sheet, '保存')).toBeDisabled();
  await button(sheet, 'Close').click();
  await openNode(page, name);
  await detail.click();
  await scrollToLast();
  await expect(last.getByRole('spinbutton')).toHaveValue('12345');
  await expect(last).toHaveAttribute('aria-setsize', '10000');
});

test('the detail editor and save control remain usable in dark mode at the minimum window size', async ({ page }) => {
  await page.setViewportSize({ width: 820, height: 560 });
  await page.emulateMedia({ colorScheme: 'dark', reducedMotion: 'reduce' });
  await expect(page.locator('html')).toHaveClass(/\bdark\b/);
  await openNode(page, '交错 A/B');
  const sheet = editor(page);
  await sheet.getByRole('tab', { name: '详细' }).click();
  const field = sheet.getByLabel('节点后等待');
  await expect(field).toBeInViewport({ ratio: 1 });
  await field.fill('275');
  const save = button(sheet, '保存');
  await expect(save).toBeInViewport({ ratio: 1 });
  await save.click();
  await expect(save).toBeDisabled();
  await button(sheet, 'Close').click();
  await expect(sheet).toBeHidden();
});
