<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { defaultRangeExtractor, useVirtualizer } from '@tanstack/vue-virtual';
import { newEventId } from '@/api/types.js';
import { moveEvent } from '@/lib/event-list.js';
import { useManagerStore } from '@/stores/useManagerStore';
import EventRow from './EventRow.vue';

const ROW_HEIGHT = 68;
const store = useManagerStore();
const viewport = ref(null);
const draggedId = ref(null);
const focusedId = ref(null);
const dropIndex = ref(null);
const events = computed(() => store.draft?.sequence?.events ?? []);
const readOnly = computed(() => store.draftReadOnly);
const draggedIndex = computed(() => draggedId.value == null ? -1
  : events.value.findIndex((event) => event.eventId === draggedId.value));
const focusedIndex = computed(() => focusedId.value == null ? -1
  : events.value.findIndex((event) => event.eventId === focusedId.value));
const estimateSize = () => ROW_HEIGHT;
const virtualizer = useVirtualizer(computed(() => {
  const retainedIndexes = [draggedIndex.value, focusedIndex.value];
  return {
    count: events.value.length,
    getScrollElement: () => viewport.value,
    estimateSize,
    overscan: 5,
    rangeExtractor: (range) => {
      // Keep the pointer capture target and focused input mounted while scrolling.
      const indexes = new Set(defaultRangeExtractor(range));
      for (const index of retainedIndexes) {
        if (index >= 0 && index < events.value.length) indexes.add(index);
      }
      return [...indexes].sort((a, b) => a - b);
    },
  };
}));
const virtualRows = computed(() => virtualizer.value.getVirtualItems());
const totalHeight = computed(() => virtualizer.value.getTotalSize());

function issueMap(issues) {
  const map = new Map();
  for (const issue of issues) {
    if (issue.eventIndex == null) continue;
    const previous = map.get(issue.eventIndex);
    map.set(issue.eventIndex, previous ? `${previous}；${issue.message}` : issue.message);
  }
  return map;
}
const warningByIndex = computed(() => issueMap(store.draftWarnings));
const errorByIndex = computed(() => issueMap(store.draftErrors));

function updateAt(index, partial) {
  const next = events.value.slice();
  next[index] = { ...next[index], ...partial };
  store.setEvents(next);
}

function duplicate(index) {
  const next = events.value.slice();
  next.splice(index + 1, 0, { ...next[index], eventId: newEventId() });
  store.setEvents(next);
}

function removeAt(index) {
  const next = events.value.slice();
  next.splice(index, 1);
  store.setEvents(next);
}

let scrollFrame = null;
let scrollSpeed = 0;
let pointerId = null;
let captureTarget = null;
let pointerY = 0;
let startY = 0;
let moved = false;

function updateDropTarget() {
  const rect = viewport.value.getBoundingClientRect();
  const offset = pointerY - rect.top + viewport.value.scrollTop;
  dropIndex.value = Math.max(0, Math.min(events.value.length - 1, Math.floor(offset / ROW_HEIGHT)));
  scrollSpeed = pointerY < rect.top + 40 ? -10 : pointerY > rect.bottom - 40 ? 10 : 0;
}

function scrollDuringDrag() {
  if (!draggedId.value || !viewport.value || !scrollSpeed) {
    scrollFrame = null;
    return;
  }
  viewport.value.scrollTop += scrollSpeed;
  updateDropTarget();
  scrollFrame = requestAnimationFrame(scrollDuringDrag);
}

function startDrag(index, event) {
  if (readOnly.value || event.button !== 0) return;
  draggedId.value = events.value[index].eventId;
  pointerId = event.pointerId;
  pointerY = startY = event.clientY;
  moved = false;
  captureTarget = event.currentTarget;
  captureTarget.setPointerCapture(pointerId);
  window.addEventListener('pointermove', dragMove);
  window.addEventListener('pointerup', drop);
  window.addEventListener('pointercancel', finishDrag);
  window.addEventListener('blur', finishDrag);
}

function dragMove(event) {
  if (event.pointerId !== pointerId || draggedIndex.value < 0) return;
  event.preventDefault();
  pointerY = event.clientY;
  if (Math.abs(pointerY - startY) > 4) moved = true;
  if (!moved) return;
  updateDropTarget();
  if (scrollSpeed && scrollFrame == null) scrollFrame = requestAnimationFrame(scrollDuringDrag);
}

function finishDrag() {
  if (scrollFrame != null) cancelAnimationFrame(scrollFrame);
  scrollFrame = null;
  scrollSpeed = 0;
  window.removeEventListener('pointermove', dragMove);
  window.removeEventListener('pointerup', drop);
  window.removeEventListener('pointercancel', finishDrag);
  window.removeEventListener('blur', finishDrag);
  if (captureTarget?.hasPointerCapture(pointerId)) captureTarget.releasePointerCapture(pointerId);
  captureTarget = null;
  pointerId = null;
  draggedId.value = null;
  dropIndex.value = null;
}

function drop(event) {
  if (event.pointerId !== pointerId) return;
  if (moved && !readOnly.value && draggedIndex.value >= 0 && dropIndex.value != null) {
    store.setEvents(moveEvent(events.value, draggedIndex.value, dropIndex.value));
  }
  finishDrag();
}

watch(() => store.draft, () => {
  finishDrag();
  focusedId.value = null;
  if (viewport.value) viewport.value.scrollTop = 0;
});
onBeforeUnmount(finishDrag);
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

    <div
      ref="viewport"
      class="min-h-0 flex-1 overflow-y-auto overscroll-contain"
      data-event-viewport
    >
      <div v-if="events.length" role="list" aria-label="按键事件" class="relative w-full" :style="{ height: `${totalHeight}px` }">
        <div
          v-for="row in virtualRows"
          :key="events[row.index].eventId"
          role="listitem"
          :aria-posinset="row.index + 1"
          :aria-setsize="events.length"
          :data-event-index="row.index"
          class="absolute top-0 left-0 w-full"
          :class="{ 'ring-1 ring-inset ring-primary': dropIndex === row.index }"
          :style="{ height: `${ROW_HEIGHT}px`, transform: `translateY(${row.start}px)` }"
          @focusin="focusedId = events[row.index].eventId"
          @focusout="(event) => { if (!event.currentTarget.contains(event.relatedTarget)) focusedId = null; }"
        >
          <EventRow
            :index="row.index"
            :event="events[row.index]"
            :read-only="readOnly"
            :warning-text="warningByIndex.get(row.index) || ''"
            :error-text="errorByIndex.get(row.index) || ''"
            @update="(partial) => updateAt(row.index, partial)"
            @duplicate="duplicate(row.index)"
            @remove="removeAt(row.index)"
            @reorderstart="startDrag(row.index, $event)"
          />
        </div>
      </div>
      <div v-else class="flex h-24 items-center justify-center text-sm text-muted-foreground">
        暂无事件。可切换到「录制」录入。
      </div>
    </div>
  </div>
</template>
