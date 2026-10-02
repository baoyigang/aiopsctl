import {describe,it,expect,vi,afterEach} from 'vitest';
import {request} from './api';
afterEach(()=>vi.unstubAllGlobals());
describe('API client',()=>{
 it('scopes writes and attaches CSRF header',async()=>{const fetch=vi.fn().mockResolvedValue({ok:true,json:async()=>({id:'a'})});vi.stubGlobal('fetch',fetch);expect(await request('demo','assets/a',{name:'中文'},'PUT')).toEqual({id:'a'});expect(fetch).toHaveBeenCalledWith('/api/demo/assets/a',expect.objectContaining({method:'PUT',headers:expect.objectContaining({'X-Aiops-Client':'workbench'})}));});
 it('surfaces backend rejection',async()=>{vi.stubGlobal('fetch',vi.fn().mockResolvedValue({ok:false,status:400,json:async()=>({error:'不允许的分组字段'})}));await expect(request('real','charts/query',{})).rejects.toThrow('不允许的分组字段')});
});
