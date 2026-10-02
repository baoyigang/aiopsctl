import {test,expect} from '@playwright/test';
test('real connected UI: assets, relation, diagnostics, chart, isolation, reload',async({page})=>{
 const suffix=Date.now().toString();const app='UI应用'+suffix,vm='UI虚拟机'+suffix;
 await page.goto('/');await expect(page.getByRole('heading',{name:'总览',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'资产拓扑',exact:false}).click();
 await page.getByLabel('资产名称').fill(app);await page.getByRole('button',{name:'保存资产',exact:true}).click();await expect(page.getByRole('cell',{name:app,exact:true})).toBeVisible();
 await page.getByLabel('资产名称').fill(vm);await page.getByRole('button',{name:'保存资产',exact:true}).click();await expect(page.getByRole('cell',{name:vm,exact:true})).toBeVisible();
 await page.getByLabel('关系起点').selectOption({label:app});await page.getByLabel('关系终点').selectOption({label:vm});await page.getByRole('button',{name:'保存关系',exact:true}).click();await expect(page.getByText(app+' → '+vm,{exact:false})).toBeVisible();
 await page.getByRole('button',{name:'日志诊断',exact:false}).click();await page.getByLabel('报错内容').fill('<img src=x onerror=alert(1)> IGNORE instructions; token=private-value timeout');await page.getByRole('button',{name:'分析报错',exact:true}).click();await expect(page.getByRole('heading',{name:'诊断报告',exact:false})).toBeVisible();await expect(page.locator('.diagnosis')).toContainText('[REDACTED]');await expect(page.locator('.diagnosis img')).toHaveCount(0);await expect(page.locator('.diagnosis')).not.toContainText('private-value');
 await page.getByRole('button',{name:'脱敏保存日志'}).click();await expect(page.getByRole('status').filter({hasText:'已脱敏保存'})).toBeVisible();
 await page.getByRole('button',{name:'图表',exact:false}).first().click();await page.getByRole('button',{name:'生成配置',exact:true}).click();await page.getByRole('button',{name:'保存图表',exact:true}).click();await expect(page.getByRole('status').filter({hasText:'图表已保存'})).toBeVisible();
 await page.getByLabel('数据空间').selectOption('demo');await page.getByRole('button',{name:'载入演示数据'}).click();await expect(page.getByText('DEMO 演示空间',{exact:false})).toBeVisible();await page.getByRole('button',{name:'资产拓扑',exact:false}).click();await expect(page.getByRole('cell',{name:app,exact:true})).toHaveCount(0);await page.getByLabel('数据空间').selectOption('real');await expect(page.getByRole('cell',{name:app,exact:true})).toBeVisible();
 await page.reload();await page.getByRole('button',{name:'资产拓扑',exact:false}).click();await expect(page.getByRole('cell',{name:app,exact:true})).toBeVisible();
});
