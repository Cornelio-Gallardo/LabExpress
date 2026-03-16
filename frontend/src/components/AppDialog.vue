<template>
  <teleport to="body">
    <transition name="dialog-fade">
      <div v-if="state" class="dialog-backdrop" @mousedown.self="_respond(false)">
        <div class="dialog-box" role="dialog" :aria-modal="true">

          <!-- Icon -->
          <div class="dialog-icon" :class="iconClass">
            <svg v-if="state.type === 'confirm'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v4m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/>
            </svg>
            <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <path stroke-linecap="round" d="M12 8v4m0 4h.01"/>
            </svg>
          </div>

          <!-- Content -->
          <div class="dialog-content">
            <div class="dialog-title">{{ state.title }}</div>
            <div class="dialog-message">{{ state.message }}</div>
          </div>

          <!-- Actions -->
          <div class="dialog-actions">
            <template v-if="state.type === 'confirm'">
              <button class="dialog-btn dialog-btn-cancel" @click="_respond(false)">Cancel</button>
              <button class="dialog-btn dialog-btn-confirm" @click="_respond(true)" ref="confirmBtn">Confirm</button>
            </template>
            <template v-else>
              <button class="dialog-btn dialog-btn-confirm" @click="_respond(true)" ref="confirmBtn">OK</button>
            </template>
          </div>

        </div>
      </div>
    </transition>
  </teleport>
</template>

<script setup>
import { computed, ref, watch, nextTick } from 'vue'
import { useDialog } from '../composables/useDialog'

const { _dialogState: state, _respond } = useDialog()
const confirmBtn = ref(null)

const iconClass = computed(() =>
  state.value?.type === 'confirm' ? 'icon-warning' : 'icon-info'
)

watch(state, async (val) => {
  if (val) {
    await nextTick()
    confirmBtn.value?.focus()
  }
})
</script>

<style scoped>
.dialog-backdrop {
  position: fixed; inset: 0; z-index: 99999;
  background: rgba(0,0,0,0.45);
  display: flex; align-items: center; justify-content: center;
  padding: 16px;
}
.dialog-box {
  background: white;
  border-radius: 14px;
  box-shadow: 0 20px 60px rgba(0,0,0,0.2);
  width: 100%; max-width: 420px;
  padding: 28px 28px 22px;
  display: flex; flex-direction: column; align-items: center; gap: 14px;
  text-align: center;
}
.dialog-icon {
  width: 52px; height: 52px;
  border-radius: 50%;
  display: flex; align-items: center; justify-content: center;
  flex-shrink: 0;
}
.dialog-icon svg { width: 26px; height: 26px; }
.icon-warning { background: #fef3c7; color: #d97706; }
.icon-info    { background: #dbeafe; color: #2563eb; }
.dialog-content { display: flex; flex-direction: column; gap: 6px; }
.dialog-title   { font-size: 16px; font-weight: 700; color: #111827; }
.dialog-message { font-size: 14px; color: #4b5563; line-height: 1.5; }
.dialog-actions {
  display: flex; gap: 10px; justify-content: center;
  width: 100%; margin-top: 4px;
}
.dialog-btn {
  padding: 9px 24px; border-radius: 8px; font-size: 14px; font-weight: 600;
  cursor: pointer; border: none; transition: all 0.15s; min-width: 90px;
}
.dialog-btn-cancel  { background: #f3f4f6; color: #374151; }
.dialog-btn-cancel:hover  { background: #e5e7eb; }
.dialog-btn-confirm { background: #1e3a8a; color: white; }
.dialog-btn-confirm:hover { background: #1e40af; }

/* Transition */
.dialog-fade-enter-active, .dialog-fade-leave-active { transition: opacity 0.15s ease; }
.dialog-fade-enter-from, .dialog-fade-leave-to { opacity: 0; }
.dialog-fade-enter-active .dialog-box, .dialog-fade-leave-active .dialog-box { transition: transform 0.15s ease; }
.dialog-fade-enter-from .dialog-box  { transform: scale(0.93); }
.dialog-fade-leave-to .dialog-box    { transform: scale(0.93); }
</style>
