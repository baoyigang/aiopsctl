export async function request<T=any>(space:string,path:string,body?:unknown,method?:string):Promise<T>{
 const response=await fetch(`/api/${space}/${path}`,{method:method??(body===undefined?'GET':'POST'),headers:{'Content-Type':'application/json','X-Aiops-Client':'workbench'},body:body===undefined?undefined:JSON.stringify(body)});
 const result=await response.json();if(!response.ok)throw new Error(result.error??`请求失败 ${response.status}`);return result;
}
export const kinds:Record<string,string>={physical:'物理机',vm:'虚拟机',kubernetes:'Kubernetes',application:'应用',middleware:'中间件',database:'数据库'};
export const relationKinds:Record<string,string>={deployed_on:'部署于',belongs_to:'归属于',calls:'人工记录调用（非观测证据）'};
