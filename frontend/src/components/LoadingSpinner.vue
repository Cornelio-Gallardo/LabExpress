<template>
  <div class="spinner-wrap">
    <div class="spinner"></div>
    <div v-if="message" class="spinner-msg">{{ message }}</div>
  </div>
</template>

<script setup>
defineProps({ message: { type: String, default: '' } })
</script>

<style scoped>
.spinner-wrap { display:flex; flex-direction:column; align-items:center; justify-content:center; gap:12px; padding:40px 24px; }
.spinner      { width:32px; height:32px; border:3px solid var(--border, #e2e8f0); border-top-color:var(--primary, #3b82f6); border-radius:50%; animation:spin 0.8s linear infinite; }
.spinner-msg  { font-size:13px; color:var(--slate, #64748b); }
@keyframes spin { to { transform:rotate(360deg); } }
</style>
