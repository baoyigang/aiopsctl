import {defineConfig} from 'vite';
import vue from '@vitejs/plugin-vue';
export default defineConfig({
 plugins:[vue()],
 server:{host:'127.0.0.1',proxy:{'/api':{target:'http://127.0.0.1:5080',changeOrigin:true,configure(proxy){proxy.on('proxyReq',req=>req.removeHeader('origin'));}}}},
 build:{outDir:'../backend/AiOps.Api/wwwroot',emptyOutDir:true}
});
