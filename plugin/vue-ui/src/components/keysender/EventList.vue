<script setup>
import { computed } from 'vue';
import { VueDraggable } from 'vue-draggable-plus';
import { ScrollArea } from '@/components/ui/scroll-area';
import { newEventId } from '@/api/types.js';
import { useManagerStore } from '@/stores/useManagerStore';
import EventRow from './EventRow.vue';

const store = useManagerStore();

const events = computed(() => store.draft?.sequence?.events ?? []);
const readOnly = computed(() => store.draftReadOnly);

const warningByIndex = computed(() => {
  const map = new Map();
  for (const w of store.draftWarnings) {
    if (w.eventIndex == null) continue;
    const prev = map.get(w.eventIndex);
    map.set(w.eventIndex, prev ? `${prev}；${w.message}` : w.message);
  }
  return map;
});

const errorByIndex = computed(() => {
  const map = new Map();
  for (const e of store.draftErrors) {
    if (e.eventIndex == null) continue;
    const prev = map.get(e.eventIndex);
    map.set(e.eventIndex, prev ? `${prev}；${e.message}` : e.message);
  }
  return map;
});

function commit(next) {
  store.setEvents(next);
}

function onDragUpdate(list) {
  commit(list);
}

function updateAt(index, partial) {
  const next = events.value.map((e, i) => (i === index ? { ...e, ...partial } : e));
  commit(next);
}

function duplicate(index) {
  const src = events.value[index];
  const copy = { ...src, eventId: newEventId() };
  const next = [...events.value];
  next.splice(index + 1, 0, copy);
  commit(next);
}

function removeAt(index) {
  commit(events.value.filter((_, i) => i !== index));
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <div class="grid shrink-0 grid-cols-[1.5rem_2rem_6.5rem_minmax(7.5rem,1fr)_6.5rem_auto] gap-1 border-b border-border bg-muted/40 px-1 py-1 text-[11px] text-muted-foreground">
      <span />
      <span class="text-center">#</span>
      <span>类型</span>
      <span>按键</span>
      <span>前等待</span>
      <span class="pr-2 text-right">操作</span>
    </div>

    <ScrollArea class="min-h-0 flex-1">
      <VueDraggable
        :model-value="events"
        :disabled="readOnly"
        handle="[data-drag-handle]"
        class="min-h-24"
        @update:model-value="onDragUpdate"
      >
        <EventRow
          v-for="(ev, index) in events"
          :key="ev.eventId"
          :index="index"
          :event="ev"
          :read-only="readOnly"
          :warning-text="warningByIndex.get(index) || ''"
          :error-text="errorByIndex.get(index) || ''"
          @update="(p) => updateAt(index, p)"
          @duplicate="duplicate(index)"
          @remove="removeAt(index)"
        />
      </VueDraggable>
      <div
        v-if="!events.length"
        class="flex h-24 items-center justify-center text-sm text-muted-foreground"
      >
        暂无事件。可切换到「录制」录入。
      </div>
    </ScrollArea>
  </div>
</template>
