<script setup lang="ts">
import {ref,onMounted,onBeforeUnmount,watch} from 'vue';
import * as echarts from 'echarts/core';
import {BarChart,PieChart} from 'echarts/charts';
import {GridComponent,TooltipComponent} from 'echarts/components';
import {CanvasRenderer} from 'echarts/renderers';
echarts.use([BarChart,PieChart,GridComponent,TooltipComponent,CanvasRenderer]);
const props=defineProps<{data:{name:string,value:number}[],type:string}>();const el=ref<HTMLElement>();let chart:echarts.ECharts|undefined;
function render(){chart?.setOption({tooltip:{renderMode:'richText'},xAxis:props.type==='bar'?{type:'category',data:props.data.map(x=>x.name)}:undefined,yAxis:props.type==='bar'?{type:'value'}:undefined,series:[{type:props.type,data:props.type==='bar'?props.data.map(x=>x.value):props.data,radius:'65%',itemStyle:{color:'#21b6a8'}}]},true);}
const resize=()=>chart?.resize();onMounted(()=>{chart=echarts.init(el.value!);render();window.addEventListener('resize',resize)});watch(()=>[props.data,props.type],render,{deep:true});onBeforeUnmount(()=>{window.removeEventListener('resize',resize);chart?.dispose()});
</script><template><div v-if="!data.length" class="empty">当前时间范围没有数据</div><div ref="el" class="chart" aria-label="统计图表"></div></template>
