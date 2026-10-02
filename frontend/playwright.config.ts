import {defineConfig} from '@playwright/test';
export default defineConfig({testDir:'./tests/ui',workers:1,use:{baseURL:'http://127.0.0.1:5080',headless:true,launchOptions:{executablePath:process.env.CHROMIUM_PATH||'/usr/bin/chromium'}},reporter:'list'});
