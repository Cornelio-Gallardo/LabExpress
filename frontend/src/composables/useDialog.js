import { ref } from 'vue'

// Shared singleton state — one dialog at a time across the entire app
const state = ref(null)

export function useDialog() {
  function alert(message, title = 'Notice') {
    return new Promise(resolve => {
      state.value = { type: 'alert', message, title, resolve }
    })
  }

  function confirm(message, title = 'Confirm') {
    return new Promise(resolve => {
      state.value = { type: 'confirm', message, title, resolve }
    })
  }

  function _respond(result) {
    state.value?.resolve(result)
    state.value = null
  }

  return { _dialogState: state, alert, confirm, _respond }
}
