<template>
  <div class="login-page">
    <div class="login-box">

      <!-- Logo — cross shape like in screenshots -->
      <div class="login-logo">
        <div class="dx7-logo dx7-logo-lg">
          <span class="dx7-d">D</span><span class="dx7-x">X</span><span class="dx7-seven">7</span>
        </div>
      </div>

      <div class="login-title">Sign In</div>
      <div class="login-sub">Access your account to view results and health records</div>

      <div v-if="error" class="login-error">{{ error }}</div>

      <div>
        <label class="login-label">Email <span class="req">*</span></label>
        <input v-model="email" type="email" class="login-input" placeholder="you@gmail.com" @keyup.enter="login" />

        <label class="login-label">Password <span class="req">*</span></label>
        <input v-model="password" type="password" class="login-input" placeholder="••••••••••••" @keyup.enter="login" />
      </div>

      <button class="login-btn" @click="login" :disabled="loading">
        {{ loading ? 'Signing in...' : 'Sign in' }}
      </button>

      <div class="login-forgot">
        <router-link to="/forgot-password">Forgot password?</router-link>
      </div>

      <div class="login-footer">
        Dx7 Clinical Information System &nbsp;·&nbsp; LABExpress
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../store/auth'
import api from '../services/api'

const router   = useRouter()
const auth     = useAuthStore()
const email    = ref('')
const password = ref('')
const error    = ref('')
const loading  = ref(false)

async function login() {
  error.value = ''
  if (!email.value || !password.value) { error.value = 'Please enter your email and password'; return }
  loading.value = true
  try {
    const { data } = await api.post('/auth/login', { email: email.value, password: password.value })
    auth.setSession(data)
    router.push('/shifts')
  } catch (err) {
    error.value = err.response?.data?.message || 'Invalid email or password'
  } finally { loading.value = false }
}
</script>

<style scoped>
.login-forgot {
  text-align: right;
  margin-top: 12px;
}
.login-forgot a {
  font-size: 13px;
  color: var(--primary-mid);
  text-decoration: none;
  font-weight: 500;
}
.login-forgot a:hover { text-decoration: underline; }
.req { color: var(--red); }
</style>