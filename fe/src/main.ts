import { createApp } from 'vue'
import App from './App.vue'
import router from '@/common/router'
import '@/styles/global/index.scss'
import '@/common/styles/index.scss'

createApp(App).use(router).mount('#app')
