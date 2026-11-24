/* An orthographic carving traced through matching anatomical landmarks in the
   two painted studies. X,Y,Z in the construction correspond to front X,
   front height and profile X respectively, in the adult drawing's pixels. */
(() => {
  "use strict";
  const clamp=(n,l=0,h=1)=>Math.max(l,Math.min(h,n));
  const mix=(a,b,t)=>a+(b-a)*t;
  const ramp=(a,b,x)=>{const t=clamp((x-a)/(b-a));return t*t*(3-2*t);};
  function fit(x,knots) {
    let i=1;
    while(i<knots.length-1 && x>knots[i][0]) i++;
    const [a,b]=knots[i-1],[c,d]=knots[i];
    return mix(b,d,(x-a)/(c-a));
  }

  // Roll the two walls of the pinna around the same transverse crease. A
  // circular hinge carries the ear into a straight, forward-drooping tip.
  // Rotate its actual thickness too, so the back rolls over the opening.
  function foldPinna(positions,first,end,shape,kitten,side) {
    if(shape!=="slight" && shape!=="folded")return;
    const full=shape==="folded";
    const turn=full?2.54:1.23;
    const arcLength=kitten?(full?.108:.115):(full?.178:.2);
    const radius=arcLength/turn;
    for(let i=first;i<end;i+=3) {
      const x=positions[i],y=positions[i+1],z=positions[i+2],width=Math.abs(x);
      const hinge=kitten?(full?.947:.955)-.19*width:
        (side<0?2.83:2.82)-.22*width-(full?.046:0);
      const above=y-hinge;
      if(above<=0)continue;
      const angle=Math.min(above/arcLength,1)*turn;
      const tail=Math.max(0,above-arcLength);
      const sin=Math.sin(angle), cos=Math.cos(angle);
      const axisZ=kitten?.96-.75*width:(side<0?.18+.41*width:.20-.49*width);
      const depth=z-axisZ;
      // As the two lips close, the broad convex back flattens. It cannot
      // lever the entire ear upward on a short pinna.
      const thickness=kitten?.042:.075;
      const across=depth<0?thickness*Math.expm1(depth/thickness):depth;
      const forward=radius*(1-cos)+tail*sin;
      positions[i]=x+side*.17*forward;
      positions[i+1]=hinge+radius*sin+tail*cos-across*sin;
      positions[i+2]=axisZ+forward+depth*cos;
    }
  }

  // The painted ears use broad triangles when upright. Split their spans
  // before curling them, carrying every bound UV and coat coordinate with
  // the surface. The rest of the carving is copied without resampling.
  function foldEars(T,root,genes) {
    if(genes.earShape!=="slight" && genes.earShape!=="folded")return;
    const kitten=genes.age==="kitten";
    root.traverse(mesh=>{
      const original=mesh.geometry,spans=original?.userData.pinnae;
      if(!spans)return;
      const folded=new T.BufferGeometry(),channels=Object.entries(original.attributes);
      const count=original.attributes.position.count+
        spans.reduce((n,s)=>n+(s.end-s.start)*(s.steps*s.steps-1),0);
      const buffers=channels.map(([name,a])=>{
        const b=new T.BufferAttribute(new a.array.constructor(count*a.itemSize),a.itemSize,a.normalized);
        folded.setAttribute(name,b);
        return {size:a.itemSize,source:a.array,target:b.array};
      });
      let read=0,write=0;
      function copyUntil(limit) {
        for(const {size,source,target} of buffers)
          target.set(source.subarray(read*size,limit*size),write*size);
        write+=limit-read;read=limit;
      }
      const a=new T.Vector3(),b=new T.Vector3(),c=new T.Vector3();
      const p=folded.attributes.position.array,n=folded.attributes.normal.array;
      for(const {start,end,side,steps} of spans) {
        copyUntil(start);
        const begin=3*write;
        while(read<end) {
          // Barycentric lattice, shared along edges of neighbouring faces.
          function emit(i,j) {
            const u=i/steps,v=j/steps;
            for(const {size,source,target} of buffers)for(let k=0;k<size;k++) {
              const offset=read*size+k,p0=source[offset];
              target[write*size+k]=p0+(source[offset+size]-p0)*u+(source[offset+2*size]-p0)*v;
            }
            write++;
          }
          for(let j=0;j<steps;j++)for(let i=0;i<steps-j;i++) {
            emit(i,j);emit(i+1,j);emit(i,j+1);
            if(i+j+1<steps){emit(i+1,j);emit(i+1,j+1);emit(i,j+1);}
          }
          read+=3;
        }
        foldPinna(p,begin,write*3,genes.earShape,kitten,side);
        for(let i=begin;i<write*3;i+=9) {
          a.fromArray(p,i);b.fromArray(p,i+3).sub(a);c.fromArray(p,i+6).sub(a);
          b.cross(c);
          if(b.lengthSq()<1e-16)continue;
          b.normalize();
          for(let j=i;j<i+9;j+=3)b.toArray(n,j);
        }
      }
      copyUntil(original.attributes.position.count);
      folded.computeBoundingSphere();
      mesh.geometry=folded;original.dispose();
    });
  }

  function build(T,root,g,kit) {
    const kitten=g.age==="kitten";
    const head=new T.Group();
    root.add(head);
    head.name="Carved calico head";
    const positions=[],fu=[],su=[],registered=[],blend=[],profileReach=[];
    const pinnae=[];
    const eye=(g.eyes??.5)-.5, ear=(g.ears??.5)-.5, cheek=(g.cheeks??.5)-.5;
    const snout=(g.muzzle??.5)-.5, condition=(g.fullness??.5)-.5;
    const frontX=[[130,149],[193,208],[245,283],[278,329],[350,405],[414,486],[449,539],[487,596],[560,654]];
    const sideX=[[842,901],[868,934],[904,981],[948,1040],[994,1103],[1045,1189],[1120,1251],[1193,1332]];
    const frontY=[[16,84],[44,100],[126,215],[176,279],[215,331],[236,350],[253,369],[290,420],[315,451],[342,479],[370,506]];
    const sideY=[[21,83],[43,109],[130,214],[173,275],[219,333],[252,374],[278,402],[298,433],[347,481],[372,509]];
    const frontUV=(x,y)=>kitten?[fit(x,frontX)/2172,1-fit(y,frontY)/724]:[x/1536,1-y/1024];
    const sideUV=(x,y)=>kitten?[fit(x,sideX)/2172,1-fit(y,sideY)/724]:[x/1536,1-y/1024];
    const gaussian=(v,r)=>Math.exp(-(v*v)/(r*r));
    function vertex(x,y,sx,textureFront,textureSide,w,earPivot=null) {
      const v={f:textureFront,s:textureSide,w};
      const sign=x<352?-1:1;
      let dx=x-352;
      if(earPivot) {
        const above=clamp((earPivot-y)/118);
        y-=ear*43*above; dx+=sign*ear*10*above;
        sx+=ear*8*above;
      } else {
        const jaw=gaussian(y-293,54)*ramp(60,144,Math.abs(dx));
        dx+=sign*(cheek*29+condition*10)*jaw;
        const ex=sign<0?-78:68,ey=sign<0?252:238;
        const orbit=gaussian(dx-ex,49)*gaussian(y-ey,29)*ramp(1080,935,sx);
        y+=eye*(y-ey)*1.04*orbit;
        dx+=eye*(dx-ex)*.37*orbit;
        // Keep the bridge anchored while the whisker pads and lower mouth follow the snout length.
        y+=snout*51*gaussian(y-310,48)*gaussian(dx,83)*ramp(1035,892,sx)*ramp(258,306,y);
        sx-=snout*37*gaussian(y-291,39)*gaussian(dx,63)*ramp(1035,892,sx);
      }
      x=352+dx;
      v.p=kitten?[(fit(x,frontX)-404)/490,(650-fit(y,frontY))/490,(1445-fit(sx,sideX))/400]:
        [(x-352)/300,(965-y)/300,(1080-sx)/300];
      return v;
    }
    function tri(a,b,c,axis,sign) {
      const p=new T.Vector3().fromArray(a.p), q=new T.Vector3().fromArray(b.p), r=new T.Vector3().fromArray(c.p);
      const normal=q.sub(p).cross(r.sub(p));
      if(normal.lengthSq()<1e-14)return;
      if(axis && normal[axis]*sign<0) [b,c]=[c,b];
      for(const v of [a,b,c]){
        positions.push(...v.p);fu.push(...v.f);su.push(...v.s);registered.push(...(v.r||v.s));
        blend.push(v.w);profileReach.push((v.profile??1)*(1-v.w));
      }
    }
    function quad(a,b,c,d,axis,sign){tri(a,b,c,axis,sign);tri(a,c,d,axis,sign);}
    // Centre height, left silhouette and right silhouette, then the profile
    // depths of the ridge, orbit, outer cheek and occiput. Cheek tufts have
    // different heights on the two sides, as in the design.
    const rows=[
      [130,301,143,394,139,1006,1019,1044,1103],
      [147,256,164,437,151,959,984,1044,1117],
      [175,214,196,465,174,922,951,1048,1136],
      [199,195,221,480,199,894,930,1054,1147],
      [215,187,233,495,213,890,925,1057,1155],
      [228,181,248,510,227,886,925,1061,1162],
      [240,174,262,536,239,883,925,1070,1168],
      [254,167,278,515,242,871,927,1066,1173],
      [263,155,292,556,253,854,931,1085,1178],
      [278,182,289,543,273,843,926,1079,1185],
      [292,141,312,526,292,844,927,1089,1190],
      [306,155,324,505,305,852,935,1091,1188],
      [320,174,340,519,315,862,944,1097,1172],
      [332,199,344,497,332,868,953,1084,1150],
      [347,184,354,468,343,879,963,1068,1135],
      [358,232,366,440,351,892,973,1053,1119],
      [364,270,369,407,356,920,986,1040,1101]
    ];
    // On the skin, the zygomatic arch continues into one rounded jaw.
    // These rest-pose stations run from the anchored temples to the chin.
    // Columns are centre height, left X/Y, right X/Y and side depth.
    const skinOutline=[
      [199,195,221,480,199,1054], [215,187,233,492,214,1058],
      [235,178,249,503,233,1065], [254,174,266,510,252,1073],
      [274,175,284,511,271,1080], [292,181,300,507,289,1085],
      [310,189,316,498,307,1086], [326,200,332,486,323,1080],
      [340,216,344,470,336,1069], [351,233,355,453,346,1057],
      [358,248,362,435,352,1049], [364,270,369,407,356,1040]
    ];
    function skinSection(y,sign) {
      let i=1;
      while(i<skinOutline.length-1 && y>skinOutline[i][0])i++;
      const low=skinOutline[i-1], high=skinOutline[i];
      const f=clamp((y-low[0])/(high[0]-low[0]));
      const col=sign<0?1:3;
      return [col,col+1,5].map(j=>mix(low[j],high[j],f));
    }
    const centre=354;
    const ts=[0,.065,.13,.20,.27,.325,.38,.43,.48,.545,.61,.67,.73,.795,.86,.94,1];
    const facialDepth=(t,a,b,c)=>fit(t,[[0,a],[.13,a+3],[.27,mix(a,b,.48)],[.48,b],
      [.73,mix(b,c,.45)],[.90,mix(b,c,.72)],[1,c]]);
    const midRows=[];
    for(let i=0;i<rows.length;i++){
      midRows.push(rows[i]);
      if(i>1 && i<14){
        const fractions=i>7?[.25,.5,.75]:[.5];
        for(const t of fractions)midRows.push(rows[i].map((v,j)=>mix(v,rows[i+1][j],t)));
      }
    }
    function surfaceDepth(x,y) {
      const stations=midRows.map(([cy,l,ly,r,ry,a,b,c])=>{
        const left=x<centre, e=left?l:r, ey=left?ly:ry;
        const t=clamp((x-centre)/(e-centre));
        return [mix(cy,ey,t*.81+t*t*.19),facialDepth(t,a,b,c)];
      });
      let j=1;
      while(j<stations.length-1 && y>stations[j][0])j++;
      const [y0,z0]=stations[j-1],[y1,z1]=stations[j];
      return mix(z0,z1,clamp((y-y0)/(y1-y0||1)));
    }
    // Correspondences of the painted pinna roots, eyelids, nose leather,
    // mouth corners and individual whiskers. Reproject the turned painting
    // through these before releasing it onto its exact broadside silhouette.
    const marks=[
      [378,179,919,177],[406,177,949,180],[455,186,992,189],
      [387,260,920,259],[397,228,927,237],[415,213,939,222],
      [444,210,971,219],[465,206,994,215],[443,242,970,241],
      [412,251,936,252],[415,229,935,236],[426,225,944,231],
      [354,277,843,272],[362,281,852,276],[379,285,868,279],
      [362,304,852,291],[381,279,905,270],[361,320,861,315],
      [377,326,879,323],[397,318,895,317],
      [400,297,889,294],[420,293,912,295],[449,293,939,300],
      [475,297,963,310],[505,307,985,321],
      [402,305,890,304],[430,311,917,314],[452,319,938,325],
      [475,330,950,334],[500,350,970,351],
      [354,357,893,350],[496,257,1039,253],[517,303,1091,298]
    ].map(([x,y,sx,sy])=>[x,y,sx-surfaceDepth(x,y),sy-y]);
    const whiskerX=kitten?[[455,958],[465,969],[500,997],[550,1043],[595,1082]]:
      [[390,879],[400,889],[425,909],[450,935],[475,954],[500,977],[512,990]];
    const topFront=kitten?[[455,431],[475,427],[500,425.5],[525,427.5],[545,431.5],[565,437.5],[595,452]]:
      [[390,299],[405,296],[420,293],[437,291],[450,293],[475,297],[500,305],[512,311]];
    const lowFront=kitten?[[455,440],[475,442.5],[500,448],[525,458.5],[545,469.5],[565,484],[595,514]]:
      [[390,304],[405,305.5],[420,307.5],[437,312],[450,317.5],[475,330],[500,349.5],[512,361]];
    const topSide=kitten?[[960,421],[980,421.5],[1000,423.5],[1020,427],[1040,433.5],[1060,441],[1080,450.5]]:
      [[879,294],[895,294],[910,294.5],[925,297],[940,300.5],[955,306],[970,312.5],[990,324]];
    const lowSide=kitten?[[960,432],[980,437.5],[1000,445],[1020,455],[1040,467.5],[1060,482.5],[1080,501]]:
      [[879,301],[895,305],[910,310.5],[925,317.5],[940,326.5],[955,337.5],[970,350.5],[990,371]];
    function matchedSide(x,y,sx,t) {
      let total=.00000028,dx=0,dy=0;
      for(const [fx,fy,du,dv] of marks) {
        const d=(x-fx)**2+(y-fy)**2;
        const influence=1/((d+14)**1.65);
        dx+=du*influence;dy+=dv*influence;total+=influence;
      }
      const cheek=ramp(.94,.76,t);
      let uv=sideUV(sx+cheek*dx/total,y+cheek*dy/total);
      // Curves of individual guard hairs must stay monotone as they pass
      // across the abrupt points of the cheek fringe.
      if(x>382 && y>272) {
        const fx=kitten?fit(x,frontX):x,fy=kitten?fit(y,frontY):y;
        const z=fit(fx,whiskerX), a=fit(fx,topFront), b=fit(fx,lowFront);
        const u=fit(z,topSide),v=fit(z,lowSide);
        const yy=fy+mix(u-a,v-b,clamp((fy-a)/(b-a)));
        const influence=ramp(kitten?446:382,kitten?465:398,fx)*ramp(272,286,y);
        uv=[mix(uv[0],z/(kitten?2172:1536),influence),mix(uv[1],1-yy/(kitten?724:1024),influence)];
      }
      return uv;
    }
    const openThroat=g.furLength==="hairless" && !kitten;
    const throatRim=[];
    const jawRow=midRows.findIndex(row=>row[0]===332);
    for(const sign of [-1,1]) {
      const front=[],rear=[];
      for(const row of midRows) {
        const [y,l,ly,r,ry,a,b,c,d]=row;
        const edge=sign<0?l:r, edgeY=sign<0?ly:ry;
        const skin=g.furLength==="hairless" && y>199 ? skinSection(y,sign) : null;
        const band=ts.map(t=>{
          const x=mix(centre,edge,t), fy=mix(y,edgeY,t*.81+t*t*.19);
          const z=facialDepth(t,a,b,c);
          const rightEye=gaussian(fy-239,16)*gaussian(x-421,32);
          const leftEye=gaussian(fy-251,16)*gaussian(x-274,32);
          const depth=z+3.6*(rightEye+leftEye);
          const frontTex=frontUV(x,fy);
          const profileTex=sideUV(depth,fy);
          const w=sign>0?.95*ramp(.72,.98,t):0;
          let px=x,py=fy,pz=depth;
          if(skin) {
            // Keep each eye, the whisker pads and the nose anchored to the
            // original drawing; only release the outer cheek from the fur.
            const eyeDistance=sign<0 ? Math.hypot((x-274)/59,(fy-251)/27) :
                                       Math.hypot((x-421)/55,(fy-239)/29);
            const eye=1-ramp(.86,1.58,eyeDistance);
            const outside=ramp(.14,.60,t)*(1-eye);
            px+=(skin[0]-edge)*t*outside;
            py+=(skin[1]-edgeY)*(t*.81+t*t*.19)*outside;
            // Beyond the orbit the bare cheek sweeps continuously around the
            // zygomatic arch. Following each old depth rib here would put
            // horizontal shelves under the eye even with a rounded outline.
            const arc=.80*Math.pow(t,1.33)+.20*(1-Math.sqrt(1-t*t));
            const chin=ramp(296,367,y);
            const snout=gaussian(y-285,31)*gaussian(x-centre,53);
            const pads=gaussian(y-308,26)*gaussian(Math.abs(x-centre)-38,29);
            const ridge=879+15*gaussian(y-191,31)+43*chin*chin-34*snout-12*pads;
            const cheekDepth=mix(ridge,skin[2],arc);
            const transition=ramp(199,232,y);
            pz+=ramp(.10,.42,t)*(1-eye)*transition*(cheekDepth-z);
          }
          const v=vertex(px,py,pz,frontTex,profileTex,w);
          v.profile=sign>0?1:1-ramp(7,37,centre-x);
          if(sign>0)v.r=matchedSide(x,fy,depth,t);
          return v;
        });
        if(openThroat && y>332) {
          // The chin projects in front of the throat. Keep its complete
          // curving underside, cutting progressively higher behind the jaw.
          band.forEach((v,j)=>{
            const chin=1-ramp(.29,.91,ts[j]),at=front[jawRow][j];
            v.p=v.p.map((n,k)=>mix(at.p[k],n,chin));
          });
        }
        front.push(band);
        const around=[band[band.length-1]];
        for(const [width,progress] of [[.91,.38],[.67,.72],[.36,.94],[0,1]]) {
          const fx=mix(centre,edge,width),fy=mix(y,edgeY,width);
          const depth=mix(c,d,progress), py=Math.min(345,fy-5);
          const fw=sign>0?1:ramp(.70,1,progress);
          // On the ginger side keep the crown ginger rather than reusing the
          // opposite black eye. The rear has a continuous cap of dark fur.
          const sampleX=sign>0?404:clamp(215+width*12+(y-217)*.12,208,246);
          const sampleY=clamp(171+(y-130)*.30,155,210);
          // The returning surface meets the same skin station as the front.
          const px=skin ? fx+(skin[0]-edge)*width : fx;
          const hy=skin ? fy+(skin[1]-edgeY)*width : fy;
          const pz=skin ? depth+(skin[2]-c)*(1-progress) : depth;
          around.push(vertex(px,hy,pz,frontUV(sampleX,sampleY),sideUV(depth,py),fw));
        }
        rear.push(openThroat && y>332 ? rear[jawRow] : around);
      }
      if(openThroat) {
        const ring=i=>[...front[i],...rear[i].slice(1)];
        const end=ring(front.length-1);
        end.forEach((v,i)=>{
          if(sign>0 && (i===0 || i===end.length-1))return;
          throatRim.push({point:v.p});
        });
      }
      for(let i=1;i<front.length;i++){
        for(let j=1;j<ts.length;j++) {
          const a=front[i-1][j-1],b=front[i-1][j],c=front[i][j],d=front[i][j-1];
          // The underside turns behind the chin. Winding follows the jaw,
          // even once its normal points downward and away from the viewer.
          if(openThroat && i>jawRow) {
            if(sign<0){tri(a,b,c);tri(a,c,d);}else{tri(a,c,b);tri(a,d,c);}
          } else quad(a,b,c,d,'z',1);
        }
        for(let j=1;j<rear[i].length;j++)quad(rear[i-1][j-1],rear[i-1][j],rear[i][j],rear[i][j-1],'x',sign);
      }
      for(const [i,top] of [[0,1],[front.length-1,-1]]){
        if(openThroat && top<0)continue;
        const loop=[...front[i],...rear[i].slice(1)];
        const row=midRows[i], y=row[0];
        const p=vertex(centre,y+(top>0?-3:3),(row[5]+row[8])*.5,frontUV(350,y),sideUV((row[5]+row[8])*.5,clamp(y,145,342)),.25);
        for(let j=0;j<loop.length;j++)tri(p,loop[j],loop[(j+1)%loop.length],'y',top);
      }
    }

    const skullVertexCount=positions.length/3;
    // Precisely registered pinnae with a recessed inner bowl, a furred rim,
    // an ear base sunk into the temple, and a separate back plane.
    function earShell(sign) {
      const right=sign>0;
      const outer=right?[[354,141,964],[378,103,983],[411,56,1007],[447,20,1046],
        [466,55,1072],[481,113,1102],[484,174,1131],[458,169,1110],[391,149,1002]]:
        [[189,211,964],[181,172,953],[181,122,949],[195,47,954],[214,61,966],
        [258,94,984],[291,129,1000],[276,159,990],[221,186,973]];
      const inner=right?[[375,139,988],[390,108,998],[417,72,1017],[445,37,1047],[457,68,1060],
        [469,112,1081],[470,153,1093],[447,145,1084],[404,139,1013]]:
        [[198,187,967],[191,159,961],[191,124,958],[197,61,957],[208,70,967],
        [242,99,976],[272,129,991],[252,146,984],[217,169,970]];
      function pin([x,y,z],dz=0,back=false) {
        const frontX=x+(right?-2:2);
        // The front atlas contains both uniquely colored ears. Give the outer
        // surfaces the body color at the rim, avoiding the pink inner shell.
        let f=back?frontUV(right?414:249,right?87:104):frontUV(frontX,y+2);
        // When the outer surface rolls into view, project a patch of copper
        // temple fur over it. Its diagonal follows the grain toward the tip.
        if(back && !right && (g.earShape==="slight" || g.earShape==="folded")) {
          const fx=232+.43*(x-181)+.13*(165-y);
          f=frontUV(fx,367-.75*fx+.09*(y-110));
        }
        const safeSide=sideUV(z+dz,y+2);
        const v=vertex(x,y,z+dz,f,safeSide,back && right?.73:0, right?165:187);
        if(!right)v.profile=0;
        return v;
      }
      const rim=outer.map(v=>pin(v));const inside=inner.map(v=>pin(v,3));
      const bowl=pin(right?[435,107,1058]:[220,131,966],14);
      const back=pin(right?[435,112,1095]:[217,137,992],13,true);
      const behind=outer.map(v=>pin(v,2,true));
      for(let i=0;i<rim.length;i++) {
        const j=(i+1)%rim.length;
        quad(rim[i],rim[j],inside[j],inside[i],'z',1);
        tri(inside[i],inside[j],bowl,'z',1);
        tri(behind[j],behind[i],back,'z',-1);
        quad(rim[i],behind[i],behind[j],rim[j],null,1);
      }
      // The ear canal sits below the visible pinna; sink its furred pedicle
      // into the actual cranium so that the far ear connects at every yaw.
      const seam=right?[6,7,8,0]:[6,7,8,0,1];
      const bases=right?[[473,219,1104],[453,215,1079],[401,194,1030],[375,173,997]]:
        [[294,180,1050],[271,203,1042],[228,229,1040],[202,238,1036],[193,214,1035]];
      const buried=bases.map(v=>pin(v));
      for(let i=1;i<seam.length;i++) {
        quad(rim[seam[i-1]],rim[seam[i]],buried[i],buried[i-1],'z',1);
        tri(buried[i-1],buried[i],back,'z',-1);
      }
    }
    function adultDarkEar() {
      const earStart=positions.length;
      // Each section encloses the whole pinna. The open cup faces slightly
      // outward; the volume visible in profile sits behind it, over the temple.
      // Height, medial/lateral X, medial/lateral/rear depth, bowl depth,
      // then the corresponding row and silhouette in each painted study.
      const sections=[
        [37,457,460,1042,1044,1046,0,22,443,450,1044,1047],
        [44,450,463,1037,1043,1052,1,32,432,456,1039,1057],
        [57,440,467,1028,1041,1062,3,47,420,461,1033,1067],
        [74,427,471,1019,1040,1073,6,66,404,468,1026,1081],
        [97,411,474,1007,1038,1087,10,90,389,475,1016,1098],
        [119,396,475,997,1038,1098,14,112,375,480,1007,1110],
        [140,382,473,990,1040,1109,17,133,361,483,999,1121],
        [157,374,470,985,1043,1117,18,148,354,484,993,1128],
        [173,372,465,984,1046,1121,15,161,355,484,988,1134],
        [190,378,462,991,1049,1119,9,169,365,480,991,1134],
        [208,393,456,1004,1053,1110,3,172,380,476,1001,1128]
      ];
      // Fractions across the ear, into the bowl, and along its convex back.
      const sectionLoop=[
        [0,0,0],[.09,-.08,0],[.24,.38,0],[.47,1,0],[.69,.76,0],
        [.86,.19,0],[1,0,0],[1,0,0,1],[.98,0,.23,1],[.85,0,.70,1],
        [.62,0,1,1],[.34,0,.84,1],[.12,0,.49,1],[.025,0,.16,1],[0,0,0,1]
      ];
      const rings=sections.map(([y,l,r,near,outer,far,cup,py,fl,fr,sl,sr])=>{
        const rootBlend=ramp(115,169,y);
        return sectionLoop.map(([t,recess,back,furred])=>{
          const x=mix(l,r,t), lip=mix(near,outer,t);
          const depth=mix(lip+cup*recess,far,back);
          const fx=furred?fr-2:mix(fl,fr,t);
          const sideX=mix(mix(sl,sr,clamp((depth-near)/(far-near))),depth,rootBlend);
          return vertex(x,y,depth,frontUV(fx,py),sideUV(sideX,mix(py,y,rootBlend)),0,185);
        });
      });
      const count=sectionLoop.length;
      for(let i=1;i<rings.length;i++)for(let j=0;j<count;j++){
        const k=(j+1)%count;
        quad(rings[i-1][j],rings[i][j],rings[i][k],rings[i-1][k],null,1);
      }
      for(const [ring,up] of [[rings[0],1],[rings[rings.length-1],-1]]) {
        for(let i=1;i<count-1;i++)tri(ring[0],ring[i],ring[i+1],'y',up);
      }
      pinnae.push({start:earStart/3,end:positions.length/3,side:1,steps:2});
      // A low fold carries the midline crown into the medial ear root.
      const temple=[
        [346,132,1005,1058],[356,129,995,1064],[370,128,986,1074],
        [385,127,983,1081],[400,130,984,1086]
      ].map(([x,y,near,far])=>[0,.36,.68,1].map(t=>{
        const sx=mix(near,far,t),py=y+Math.pow((t-.47)*2,2)*14;
        return vertex(x,py,sx,frontUV(x,Math.max(py+3,133)),sideUV(sx,py),0);
      }));
      for(let i=1;i<temple.length;i++)for(let j=1;j<temple[i].length;j++){
        quad(temple[i-1][j-1],temple[i][j-1],temple[i][j],temple[i-1][j],'y',1);
      }
    }
    const earStart=positions.length;
    earShell(-1);
    if(!kitten)pinnae.push({start:earStart/3,end:positions.length/3,side:-1,steps:5});
    if(kitten)earShell(1);else adultDarkEar();
    // Sample the cheek itself to anchor each tapering whisker, including the
    // portion crossing the face before the free tip leaves the silhouette.
    function whisker(path) {
      const ink=frontUV(282,249);
      const edges=path.map(([x,y,width])=>{
        const z=surfaceDepth(x,y)-1.2;
        return [-1,1].map(sign=>{
          const v=vertex(x,y+width*sign,z,ink,ink,0);
          v.profile=false;
          return v;
        });
      });
      for(let i=1;i<edges.length;i++){
        quad(edges[i-1][0],edges[i-1][1],edges[i][1],edges[i][0],'z',1);
      }
    }
    if(g.furLength!=="hairless") {
      whisker([[256,337,.23],[246,345,.96],[234,355,.84],[223,366,.65],[211,382,.10]]);
      whisker([[464,322,.23],[476,330,.85],[489,340,.77],[499,349,.54],[507,357,.10]]);
    }
    const geom=new T.BufferGeometry();
    geom.userData.pinnae=pinnae;
    geom.setAttribute('position',new T.Float32BufferAttribute(positions,3));
    geom.setAttribute('frontUV',new T.Float32BufferAttribute(fu,2));
    geom.setAttribute('sideUV',new T.Float32BufferAttribute(su,2));
    geom.setAttribute('registeredSideUV',new T.Float32BufferAttribute(registered,2));
    geom.setAttribute('viewMix',new T.Float32BufferAttribute(blend,1));
    geom.setAttribute('profileReach',new T.Float32BufferAttribute(profileReach,1));
    geom.computeVertexNormals();
    if(g.furLength==="hairless") {
      // The printed skull duplicates vertices per triangle. Across bare skin
      // let neighbouring planes share light, retaining the nose and orbit
      // planes and the independently carved ear rims and whiskers.
      const normal=geom.getAttribute("normal"), position=geom.getAttribute("position");
      const groups=new Map(), perVertex=[];
      const n=new T.Vector3();
      for(let i=0;i<skullVertexCount;i++) {
        const key=position.getX(i)+"/"+position.getY(i)+"/"+position.getZ(i);
        let sum=groups.get(key);
        if(!sum) {sum=new T.Vector3();groups.set(key,sum);}
        sum.add(n.fromBufferAttribute(normal,i));
        perVertex.push(sum);
      }
      for(const sum of groups.values())sum.normalize();
      for(const v of throatRim) {
        const key=v.point.map(Math.fround).join("/");
        v.normal=groups.get(key).toArray();
      }
      for(let i=0;i<skullVertexCount;i++) {
        const y=position.getY(i);
        const amount=.88*ramp(2.62,2.42,y);
        n.fromBufferAttribute(normal,i).lerp(perVertex[i],amount).normalize();
        normal.setXYZ(i,n.x,n.y,n.z);
      }
    }
    const mesh=new T.Mesh(geom,kit.material);mesh.name='Carved skull, muzzle and listening ears';
    if(openThroat)mesh.userData.throatRim=throatRim;
    head.add(mesh);
    return head;
  }
  window.WoodHead={build,foldEars};
})();
