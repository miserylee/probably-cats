/* Eyebrows share the registered face coordinates used by the eyes. Source
   stations mark the top and bottom of the painted ink in atlas pixels. */
(() => {
  "use strict";
  const types = [
    {value:"original",label:"原画眉"}, {value:"none",label:"无眉纹"},
    {value:"fine",label:"细眉"}, {value:"bold",label:"浓眉"},
    {value:"arched",label:"弯月眉"}, {value:"points",label:"双眉点"}
  ];
  // The axis runs from the bridge towards the temple. Three heights describe
  // its arc at the inner end, midpoint and outer end, independently of the ink.
  const sheets = {
    adult:[
      {axis:[308,229,192,195,219], ink:[
        [226,222,222],[232,216,217],[241,207,210],[251,199,205],[261,193,201],
        [275,187,198],[289,184,197],[299,184,197],[305,193,197],[308,198,198],[311,198,198]]},
      {axis:[381,459,183,176,189], ink:[
        [378,189,189],[382,179,189],[385,174,187],[395,171,184],[407,170,182],
        [419,172,180],[433,176,181],[445,181,185],[454,186,188],[462,190,190]]},
      {axis:[922,995,190,181,194], ink:[
        [919,192,192],[923,186,192],[928,180,192],[938,177,188],[947,175,186],
        [959,177,185],[970,180,186],[979,184,188],[987,188,191],[998,195,195]]}
    ],
    kitten:[
      {axis:[351,262,277,278,304], ink:[
        [259,307,307],[264,301,303],[275,288,295],[286,279,289],[298,272,285],
        [309,268,282],[321,265,281],[339,264,281],[346,274,282],[351,282,282],[354,283,283]]},
      {axis:[438,519,276,265,287], ink:[
        [435,283,283],[440,276,282],[446,263,279],[456,259,275],[468,258,272],
        [478,259,271],[489,262,271],[500,268,275],[508,274,279],[516,283,286],[522,290,290]]},
      {axis:[999,1089,269,259,294], ink:[
        [996,273,273],[1001,265,272],[1010,258,271],[1022,254,268],[1035,251,265],
        [1046,253,266],[1056,258,268],[1067,267,274],[1076,276,283],[1085,289,293],[1092,299,299]]}
    ]
  };
  function registerRegion({axis}) {
    const [inner,outer,root,mid,tip]=axis;
    return `if(p.x>=${Math.min(inner,outer)}. && p.x<=${Math.max(inner,outer)}.) {
      float s=(p.x-${inner}.)/(${outer}.-${inner}.);
      float center=mix(${root}.,${tip}.,s)+${(4*mid-2*(root+tip)).toFixed(1)}*s*(1.-s);
      return vec4(s,p.y-center,0.,0.);
    }\n`;
  }
  const glsl=`
    uniform float browType;
    vec4 catBrowRegion(vec2 uv,float isKitten,float site) {
      if(browType<.5 || site<.5 || site>1.5)return vec4(-9.);
      vec2 p=vec2(uv.x,1.-uv.y)*mix(vec2(1536.,1024.),vec2(2172.,724.),isKitten);
      if(isKitten<.5){
        if(p.y<147. || p.y>236.)return vec4(-9.);
        ${sheets.adult.map(registerRegion).join("")}
      } else {
        if(p.y<226. || p.y>322.)return vec4(-9.);
        ${sheets.kitten.map(registerRegion).join("")}
      }
      return vec4(-9.);
    }
    vec3 catBrowColor(vec3 color,vec4 brow,float bare){
      if(browType<1.5 || brow.x<=0. || brow.x>=1.)return color;
      float s=brow.x;
      float y=brow.y;
      float mass=sin(3.14159*s);
      float halfWidth;
      float strength;
      if(browType>4.5) {
        // Each eye has one small, chamfered supraciliary spot. The arch
        // underneath remains visible, especially on a tortoiseshell face.
        float u=(s-.32)/.146;
        float v=(y+1.8+2.8*u)/5.0;
        float edge=max(.88*abs(u)+.36*abs(v),.36*abs(u)+.87*abs(v));
        float point=1.-smoothstep(.82,1.09,edge);
        float lightness=dot(color,vec3(.299,.587,.114));
        vec3 accent=mix(color*.72,color*.81+vec3(.24,.195,.145),1.-smoothstep(.63,.85,lightness));
        return mix(color,accent,point*mix(.95,.26,bare));
      }
      if(browType<2.5) {
        y+=1.6*mass;
        halfWidth=2.25*mass*(1.-.24*s);
        strength=.74;
      } else if(browType<3.5) {
        // A compact, full inner brow and a long tapered outer end.
        halfWidth=6.2*pow(mass,.65)*(1.-.58*s);
        y+=1.7*mass;
        strength=.87;
      } else {
        y+=8.2*mass;
        halfWidth=3.6*pow(mass,.76)*(1.-.43*s);
        strength=.82;
      }
      float coverage=1.-smoothstep(halfWidth-.55,halfWidth+.62,abs(y));
      if(bare>.5) {
        // A ridge catches light on its upper bank; the recessed lower edge
        // is the same skin pigment in shade, including on slate and cocoa skin.
        halfWidth=max(.6,.55*halfWidth);
        float valleyDistance=(y-.45*halfWidth)/1.2;
        float ridgeDistance=(y+halfWidth)/1.9;
        float valley=exp(-valleyDistance*valleyDistance);
        float ridge=exp(-ridgeDistance*ridgeDistance);
        return color*(1.+(.115*ridge-.17*valley)*mass*strength);
      }
      vec3 ink=color*vec3(.37,.31,.34);
      return mix(color,ink,coverage*strength);
    }
  `;
  const cleanedTextures=new Map();
  function sampleBounds(ink,x) {
    if(x<ink[0][0] || x>ink[ink.length-1][0])return null;
    let i=1;
    while(i<ink.length-1 && x>ink[i][0])i++;
    const [x0,top0,bottom0]=ink[i-1], [x1,top1,bottom1]=ink[i];
    const t=(x-x0)/(x1-x0);
    return [top0+(top1-top0)*t,bottom0+(bottom1-bottom0)*t];
  }
  function cleanRegion(context,ink,slope,scaleX,scaleY) {
    // Replace the old brushstroke before generating mipmaps. Walk along the
    // underlying colour facet on either side, preserving its diagonal edges.
    const left=Math.floor(ink[0][0]*scaleX)-32;
    const top=Math.floor(Math.min(...ink.map(p=>p[1]))*scaleY)-32;
    const w=Math.ceil(ink[ink.length-1][0]*scaleX)+32-left;
    const h=Math.ceil(Math.max(...ink.map(p=>p[2]))*scaleY)+32-top;
    const output=context.getImageData(left,top,w,h);
    const input=output.data.slice();
    const edge=Math.max(6,1.8/scaleY);
    for(let ix=0;ix<w;ix++) {
      const x=(left+ix+.5)/scaleX;
      const bounds=sampleBounds(ink,x);
      if(!bounds)continue;
      const from=Math.floor((bounds[0]-edge)*scaleY)-top;
      const to=Math.ceil((bounds[1]+edge)*scaleY)-top;
      for(let iy=from;iy<=to;iy++) {
        const y=(top+iy+.5)/scaleY;
        const border=Math.min(y-bounds[0],bounds[1]-y);
        const alpha=Math.min(1,Math.max(0,(border+edge)/2));
        if(!alpha)continue;
        const dest=(iy*w+ix)*4;
        const samples=[-1,1].map(sign=>{
          let dy=sign;
          for(;Math.abs(dy)<30;dy+=sign) {
            const stations=sampleBounds(ink,x+dy*slope);
            if(!stations || y+dy<stations[0]-edge || y+dy>stations[1]+edge)break;
          }
          const sx=ix+dy*slope*scaleX, sy=iy+dy*scaleY;
          const px=Math.floor(sx),py=Math.floor(sy);
          return {offset:(py*w+px)*4,fx:sx-px,fy:sy-py,distance:Math.abs(dy)};
        });
        const [a,b]=samples;
        const t=a.distance/(a.distance+b.distance);
        function channel(sample,c) {
          const k=sample.offset+c;
          const upper=input[k]+sample.fx*(input[k+4]-input[k]);
          const lower=input[k+4*w]+sample.fx*(input[k+4*w+4]-input[k+4*w]);
          return upper+sample.fy*(lower-upper);
        }
        for(let c=0;c<3;c++) {
          const low=channel(a,c), high=channel(b,c);
          output.data[dest+c]+=(low+(high-low)*t-input[dest+c])*alpha;
        }
      }
    }
    context.putImageData(output,left,top);
  }
  function cleanTexture(T,texture,age) {
    const cached=cleanedTextures.get(texture);
    if(cached)return cached.result;
    const {width,height}=texture.image;
    const canvas=document.createElement("canvas");
    canvas.width=width;
    canvas.height=height;
    const context=canvas.getContext("2d",{willReadFrequently:true});
    context.drawImage(texture.image,0,0);
    const atlasSize=age==="kitten" ? [2172,724] : [1536,1024];
    sheets[age].forEach(({ink},i)=>cleanRegion(context,ink,[-.49,-.30,-.66][i],width/atlasSize[0],height/atlasSize[1]));
    const result=new T.CanvasTexture(canvas);
    result.anisotropy=texture.anisotropy;
    result.colorSpace=texture.colorSpace;
    const release=()=>{
      texture.removeEventListener("dispose",release);
      cleanedTextures.delete(texture);
      result.dispose();
    };
    cleanedTextures.set(texture,{result,release});
    texture.addEventListener("dispose",release);
    return result;
  }
  function bind(T,shader,genes) {
    const index=types.findIndex(option=>option.value===genes.browType);
    shader.uniforms.browType={value:Math.max(0,index)};
    if(index<=0)return;
    const age=genes.age==="kitten" ? "kitten" : "adult";
    for(const name of ["paint","finish","fields"]) {
      const slot=shader.uniforms[name];
      slot.value=cleanTexture(T,slot.value,age);
    }
  }
  function dispose() {
    for(const cached of cleanedTextures.values())cached.release();
  }
  window.CatBrows={types,glsl,bind,dispose};
})();
