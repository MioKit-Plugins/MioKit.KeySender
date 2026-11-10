<script setup>
import { computed } from 'vue';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { ScrollArea } from '@/components/ui/scroll-area';
import { useManagerStore } from '@/stores/useManagerStore';
import EventList from './EventList.vue';
import RecorderPanel from './RecorderPanel.vue';

const store = useManagerStore();

const open = computed({
  get: () => !!store.draft || store.draftLoading,
  set: (v) => {
    if (!v) store.requestCloseEditor();
  },
});

const draft = computed(() => store.draft);
const readOnly = computed(() => store.draftReadOnly);
const eventCount = computed(() => draft.value?.sequence?.events?.length ?? 0);

const hardErrorSummary = computed(() =>
  (store.draftErrors ?? []).map((e) => e.message).filter(Boolean).slice(0, 2).join('；'));

const warningSummary = computed(() =>
  (store.draftWarnings ?? []).map((w) => w.message).filter(Boolean).slice(0, 2).join('；'));

const title = computed(() => {
  if (!draft.value) return '编辑节点';
  if (!draft.value.nodeId) return '新建节点';
  return draft.value.name || '编辑节点';
});
</script>

<template>
  <Sheet v-model:open="open">
    <SheetContent
      side="right"
      class="flex w-full flex-col gap-0 p-0 sm:max-w-2xl"
      @interact-outside="(e) => {
        e.preventDefault();
        store.requestCloseEditor();
      }"
      @escape-key-down="(e) => {
        e.preventDefault();
        store.requestCloseEditor();
      }"
    >
      <template v-if="draft">
        <SheetHeader class="shrink-0 space-y-0 border-b border-border bg-muted/20 px-5 py-4 text-left">
          <div class="flex items-start justify-between gap-3 pr-8">
            <div class="min-w-0 space-y-1.5">
              <SheetTitle class="truncate text-base">{{ title }}</SheetTitle>
              <SheetDescription class="sr-only">
                编辑按键节点
              </SheetDescription>
            </div>
            <Badge v-if="readOnly" variant="secondary">内置 · 只读</Badge>
            <Badge v-else-if="store.isDirty" variant="outline">未保存</Badge>
          </div>
        </SheetHeader>

        <div class="flex h-12 shrink-0 items-center gap-2 border-b border-border px-5">
          <template v-if="readOnly">
            <Button size="sm" class="min-w-28" @click="store.copySelectedOrDraft()">
              复制为自定义
            </Button>
            <span class="text-xs text-muted-foreground">可查看事件，不能修改或录制</span>
          </template>
          <template v-else>
            <Button
              size="sm"
              class="min-w-20"
              :disabled="store.saving || store.draftLoading || store.recordingInProgress || !store.isDirty"
              @click="store.saveDraft()"
            >
              {{ store.saving ? '保存中' : '保存' }}
            </Button>
            <Button
              size="sm"
              variant="outline"
              class="min-w-16"
              :disabled="!draft.nodeId || store.saving || store.recordingInProgress"
              @click="store.copySelectedOrDraft()"
            >
              复制
            </Button>
            <Button
              size="sm"
              variant="destructive"
              class="min-w-16"
              :disabled="!draft.nodeId || !draft.canDelete || store.saving || store.recordingInProgress"
              @click="store.requestDelete([draft.nodeId])"
            >
              删除
            </Button>
            <span class="ml-auto text-xs tabular-nums text-muted-foreground">
              {{ eventCount }} 条事件
            </span>
          </template>
        </div>

        <div
          v-if="hardErrorSummary || warningSummary"
          class="shrink-0 border-b border-border px-5 py-2 text-xs leading-5"
        >
          <p v-if="hardErrorSummary" class="text-destructive">硬错误：{{ hardErrorSummary }}</p>
          <p v-else class="text-warning-foreground dark:text-warning">
            提示：{{ warningSummary }}
          </p>
        </div>

        <Tabs default-value="record" class="flex min-h-0 flex-1 flex-col gap-0">
          <div class="shrink-0 border-b border-border px-5 py-3">
            <TabsList class="grid w-full max-w-xs grid-cols-2">
              <TabsTrigger value="record">录制</TabsTrigger>
              <TabsTrigger value="detail">详细</TabsTrigger>
            </TabsList>
          </div>

          <TabsContent value="record" class="mt-0 min-h-0 flex-1 data-[state=inactive]:hidden">
            <ScrollArea class="h-full">
              <div class="space-y-5 p-5">
                <div class="grid gap-4">
                  <div class="space-y-1.5">
                    <Label for="sheet-node-name">名称</Label>
                    <Input
                      id="sheet-node-name"
                      :model-value="draft.name"
                      :disabled="readOnly"
                      maxlength="100"
                      @update:model-value="(v) => store.patchDraft({ name: v })"
                    />
                  </div>
                  <div class="space-y-1.5">
                    <Label for="sheet-node-desc">描述</Label>
                    <Textarea
                      id="sheet-node-desc"
                      class="min-h-20 resize-none"
                      :model-value="draft.description"
                      :disabled="readOnly"
                      @update:model-value="(v) => store.patchDraft({ description: v })"
                    />
                  </div>
                </div>
                <RecorderPanel />
              </div>
            </ScrollArea>
          </TabsContent>

          <TabsContent
            value="detail"
            class="mt-0 flex min-h-0 flex-1 flex-col data-[state=inactive]:hidden"
          >
            <div class="grid shrink-0 gap-3 border-b border-border p-5 md:grid-cols-2">
              <div class="space-y-1.5">
                <Label for="before-delay">节点前等待</Label>
                <div class="relative">
                  <Input
                    id="before-delay"
                    class="pr-8 tabular-nums"
                    type="number"
                    min="0"
                    :disabled="readOnly"
                    :model-value="draft.beforeDelayMs"
                    @update:model-value="(v) => store.patchDraft({ beforeDelayMs: Number(v) || 0 })"
                  />
                  <span class="pointer-events-none absolute top-1/2 right-2 -translate-y-1/2 text-[10px] text-muted-foreground">ms</span>
                </div>
              </div>
              <div class="space-y-1.5">
                <Label for="after-delay">节点后等待</Label>
                <div class="relative">
                  <Input
                    id="after-delay"
                    class="pr-8 tabular-nums"
                    type="number"
                    min="0"
                    :disabled="readOnly"
                    :model-value="draft.afterDelayMs"
                    @update:model-value="(v) => store.patchDraft({ afterDelayMs: Number(v) || 0 })"
                  />
                  <span class="pointer-events-none absolute top-1/2 right-2 -translate-y-1/2 text-[10px] text-muted-foreground">ms</span>
                </div>
              </div>
            </div>

            <div v-if="store.draftLoading" class="flex flex-1 items-center justify-center text-sm text-muted-foreground">
              正在加载详情…
            </div>
            <EventList v-else class="min-h-0 flex-1" />
          </TabsContent>
        </Tabs>
      </template>
      <template v-else-if="store.draftLoading">
        <SheetHeader class="shrink-0 border-b border-border px-5 py-4 text-left">
          <SheetTitle>正在加载节点</SheetTitle>
          <SheetDescription>正在读取节点配置</SheetDescription>
        </SheetHeader>
        <div class="flex flex-1 items-center justify-center text-sm text-muted-foreground">
          正在加载详情…
        </div>
      </template>
    </SheetContent>
  </Sheet>
</template>
