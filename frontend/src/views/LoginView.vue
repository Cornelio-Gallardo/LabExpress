<template>
  <div class="login-page">
    <div class="login-box">

      <div class="login-logo">
        <div class="dx7-logo dx7-logo-lg">
          <span class="dx7-d">D</span><span class="dx7-x">X</span><span class="dx7-seven">7</span>
        </div>
      </div>

      <div class="login-title">Sign In</div>
      <div class="login-sub">Access your account to view results and health records</div>

      <div v-if="error" class="login-error">{{ error }}</div>

      <!-- SSO Buttons -->
      <div class="sso-row">
        <div class="sso-google-wrapper" :class="{ 'sso-loading': ssoLoading === 'google' }">
          <div id="g-signin-container"></div>
          <div v-if="ssoLoading === 'google'" class="sso-btn sso-google sso-overlay">
            <svg width="18" height="18" viewBox="0 0 48 48"><path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.08 17.74 9.5 24 9.5z"/><path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/><path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/><path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-3.59-13.46-8.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/></svg>
            Signing in…
          </div>
        </div>
        <button class="sso-btn sso-facebook" @click="loginWithFacebook" :disabled="ssoLoading === 'facebook'">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="#1877F2"><path d="M24 12.073C24 5.405 18.627 0 12 0S0 5.405 0 12.073C0 18.1 4.388 23.094 10.125 24v-8.437H7.078v-3.49h3.047V9.41c0-3.025 1.792-4.697 4.533-4.697 1.312 0 2.686.236 2.686.236v2.97h-1.513c-1.491 0-1.956.93-1.956 1.886v2.267h3.328l-.532 3.49h-2.796V24C19.612 23.094 24 18.1 24 12.073z"/></svg>
          {{ ssoLoading === 'facebook' ? 'Signing in…' : 'Continue with Facebook' }}
        </button>
      </div>

      <div class="sso-divider"><span>or sign in with email</span></div>

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
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../store/auth'
import api from '../services/api'

const GOOGLE_CLIENT_ID = '701538152632-ofjikke4g80d8mi10dkej8kjlacilr3j.apps.googleusercontent.com'
const FACEBOOK_APP_ID  = '26873476918911060'

const router     = useRouter()
const auth       = useAuthStore()
const email      = ref('')
const password   = ref('')
const error      = ref('')
const loading    = ref(false)
const ssoLoading = ref(null)

async function login() {
  error.value = ''
  if (!email.value || !password.value) { error.value = 'Please enter your email and password'; return }
  loading.value = true
  try {
    const { data } = await api.post('/auth/login', { email: email.value, password: password.value })
    auth.setSession(data)
    router.push('/dashboard')
  } catch (err) {
    error.value = err.response?.data?.message || 'Invalid email or password'
  } finally { loading.value = false }
}

async function handleSsoToken(provider, token) {
  ssoLoading.value = provider
  error.value = ''
  try {
    const { data } = await api.post('/auth/external', { provider, token })
    auth.setSession(data)
    router.push('/dashboard')
  } catch (err) {
    error.value = err.response?.data?.message || `${provider} sign-in failed. Try email login.`
  } finally { ssoLoading.value = null }
}

function initGoogleButton() {
  if (!window.google?.accounts?.id) return
  window.google.accounts.id.initialize({
    client_id: GOOGLE_CLIENT_ID,
    callback: (response) => {
      if (response.credential) handleSsoToken('google', response.credential)
    }
  })
  const container = document.getElementById('g-signin-container')
  if (container) {
    window.google.accounts.id.renderButton(container, {
      theme: 'outline', size: 'large', text: 'continue_with', width: 340
    })
  }
}

function loginWithFacebook() {
  if (!window.FB) { error.value = 'Facebook SDK not loaded yet. Please wait a moment and try again.'; return }
  ssoLoading.value = 'facebook'
  error.value = ''
  window.FB.login((response) => {
    if (response.authResponse?.accessToken) {
      handleSsoToken('facebook', response.authResponse.accessToken)
    } else {
      ssoLoading.value = null
      error.value = 'Facebook sign-in was cancelled.'
    }
  }, { scope: 'public_profile,email' })
}

onMounted(() => {
  if (!document.getElementById('google-gsi-script')) {
    const s = document.createElement('script')
    s.id = 'google-gsi-script'
    s.src = 'https://accounts.google.com/gsi/client'
    s.async = true; s.defer = true
    s.onload = initGoogleButton
    document.head.appendChild(s)
  } else {
    initGoogleButton()
  }
  if (!document.getElementById('facebook-jssdk')) {
    window.fbAsyncInit = function () {
      window.FB.init({ appId: FACEBOOK_APP_ID, cookie: true, xfbml: false, version: 'v19.0' })
    }
    const s = document.createElement('script')
    s.id = 'facebook-jssdk'
    s.src = 'https://connect.facebook.net/en_US/sdk.js'
    s.async = true; s.defer = true
    document.head.appendChild(s)
  }
})
</script>

<style scoped>
.sso-row { display:flex; flex-direction:column; gap:10px; margin-bottom:4px; }
.sso-google-wrapper { position:relative; min-height:44px; }
.sso-google-wrapper #g-signin-container { display:flex; justify-content:center; }
.sso-overlay {
  position:absolute; inset:0; pointer-events:none;
  display:flex; align-items:center; justify-content:center;
  background:white; border-radius:8px; opacity:.85;
}
.sso-btn {
  display:flex; align-items:center; justify-content:center; gap:10px;
  width:100%; padding:11px 16px; border-radius:8px;
  font-size:14px; font-weight:600; cursor:pointer; transition:all .15s;
  border:1.5px solid #e5e7eb; background:white; color:#374151;
}
.sso-btn:hover:not(:disabled) { background:#f9fafb; border-color:#d1d5db; box-shadow:0 1px 4px rgba(0,0,0,.06); }
.sso-btn:disabled { opacity:.6; cursor:not-allowed; }
.sso-divider {
  display:flex; align-items:center; gap:10px;
  margin:18px 0 4px; color:#9ca3af; font-size:12px;
}
.sso-divider::before, .sso-divider::after { content:''; flex:1; height:1px; background:#e5e7eb; }
.login-forgot { text-align:right; margin-top:12px; }
.login-forgot a { font-size:13px; color:var(--primary-mid); text-decoration:none; font-weight:500; }
.login-forgot a:hover { text-decoration:underline; }
.req { color:var(--red); }
</style>