/* Body surfaces registered to the two orthographic paintings. Ring data are
   (height in the painting, left, right, anterior, posterior), in native pixels.
   The front and profile share one spatial point per vertex and one stable
   material coordinate system across all body-condition values. */
(() => {
  "use strict";
  const TAU = Math.PI*2;
  const clamp = (n,a=0,b=1) => Math.max(a,Math.min(b,n));
  const smooth=(a,b,t)=>{t=clamp((t-a)/(b-a));return t*t*(3-2*t);};
  // At a skin junction use the nearest surface normal of the actual body
  // pose. Areas outside the narrow fillet keep their own curvature.
  function blendSkinContact(T,geometry,body,origin,reach,fillet,transform=null,influence=null) {
    const {position,normal}=geometry.attributes;
    const torso=body.attributes;
    const triangles=[];
    const count=body.index ? body.index.count : torso.position.count;
    for(let i=0;i<count;i+=3) {
      const ids=body.index ? [body.index.getX(i),body.index.getX(i+1),body.index.getX(i+2)] : [i,i+1,i+2];
      const corners=ids.map(id=>new T.Vector3().fromBufferAttribute(torso.position,id));
      if(corners.every(p=>p.distanceToSquared(origin)>reach*reach))continue;
      const bounds=new T.Box3().setFromPoints(corners).expandByScalar(fillet);
      triangles.push({face:new T.Triangle(...corners),bounds,normals:ids.map(id=>new T.Vector3().fromBufferAttribute(torso.normal,id))});
    }
    const p=new T.Vector3(), surface=new T.Vector3(), near=new T.Vector3(), weights=new T.Vector3();
    const skin=new T.Vector3(), local=new T.Vector3();
    const inverse=transform?.clone().invert();
    const contact=transform?new Float32Array(position.count*4):null;
    for(let i=0;i<position.count;i++) {
      p.fromBufferAttribute(position,i);
      const weight=influence?influence(p):1;
      if(transform)p.applyMatrix4(transform).toArray(contact,4*i);
      if(weight<=0)continue;
      if(p.distanceToSquared(origin)>reach*reach)continue;
      let nearest=null,distance=fillet*fillet;
      for(const tri of triangles) {
        if(!tri.bounds.containsPoint(p))continue;
        tri.face.closestPointToPoint(p,surface);
        const d=p.distanceToSquared(surface);
        if(d>=distance)continue;
        nearest=tri;distance=d;near.copy(surface);
      }
      if(!nearest)continue;
      nearest.face.getBarycoord(near,weights);
      const [a,b,c]=nearest.normals;
      skin.copy(a).multiplyScalar(weights.x).addScaledVector(b,weights.y).addScaledVector(c,weights.z).normalize();
      if(inverse)skin.transformDirection(inverse);
      const share=weight*(1-smooth(0,fillet,Math.sqrt(distance)));
      if(contact)contact[4*i+3]=share;
      local.fromBufferAttribute(normal,i).lerp(skin,share).normalize();
      normal.setXYZ(i,local.x,local.y,local.z);
    }
    if(contact)geometry.setAttribute("throatContact",new T.BufferAttribute(contact,4));
  }
  function build(T,root,g,kit) {
    const baby=g.age==="kitten";
    const chart=baby ? {w:2172,h:724,x:404,z:1445,floor:650,xyUnit:490,zUnit:400}
                     : {w:1536,h:1024,x:352,z:1080,floor:965,xyUnit:300,zUnit:300};
    const fullness=clamp(g.fullness ?? .5)-.5;
    const vec=(x,y,z)=>new T.Vector3(x,y,z);
    function point(fx,py,sx) {
      return vec((fx-chart.x)/chart.xyUnit,(chart.floor-py)/chart.xyUnit,(chart.z-sx)/chart.zUnit);
    }
    function assemble(name,rings,reverse=false) {
      const pos=[],front=[],side=[],blend=[],ind=[];
      const circumference=rings[0].length;
      for(const ring of rings) {
        for(const vertex of ring) {
          const {p,fu,sv,fv,mi}=vertex;
          pos.push(p.x,p.y,p.z);
          front.push(fu/chart.w,1-fv/chart.h);
          side.push(sv/chart.w,1-(vertex.sy??fv)/chart.h);
          blend.push(mi);
        }
      }
      function tri(a,b,c) { if(reverse)ind.push(a,c,b);else ind.push(a,b,c); }
      for(let r=0;r<rings.length-1;r++) for(let n=0;n<circumference;n++) {
        const a=r*circumference+n,b=r*circumference+(n+1)%circumference;
        const c=a+circumference,d=b+circumference;
        if((r+n)%2===0){tri(a,b,c);tri(b,d,c);}
        else {tri(a,b,d);tri(a,d,c);}
      }
      for(let i=1;i<circumference-1;i++) {
        tri(0,i+1,i);
        const top=(rings.length-1)*circumference;
        tri(top,top+i,top+i+1);
      }
      const geom=new T.BufferGeometry();
      geom.setAttribute('position',new T.Float32BufferAttribute(pos,3));
      geom.setAttribute('frontUV',new T.Float32BufferAttribute(front,2));
      geom.setAttribute('sideUV',new T.Float32BufferAttribute(side,2));
      geom.setAttribute('viewMix',new T.Float32BufferAttribute(blend,1));
      geom.setIndex(ind); geom.computeVertexNormals();geom.computeBoundingSphere();
      const mesh=new T.Mesh(geom,kit.material);mesh.name=name;root.add(mesh);
    }
    function vertex(fx,fy,sx,cos,warp) {
      const p=point(fx,fy,sx);
      const v={p,fu:fx,fv:fy,sv:sx,sy:fy,mi:1-smooth(.22,.75,cos)};
      if(warp)warp(v,cos);
      return v;
    }
    function upright(name,rows,warp,sides=20) {
      const rings=rows.map(([fy,l,r,f,b])=>Array.from({length:sides},(_,i)=>{
        const a=TAU*i/sides, sine=Math.sin(a), cosine=Math.cos(a);
        const fx=(l+r)/2+(r-l)*.5*sine, sx=(b+f)/2-(b-f)*.5*cosine;
        return vertex(fx,fy,sx,cosine,warp);
      }));
      assemble(name,rings);
    }
    if(!baby) {
      // The two projecting tiers in the illustration are chest fur. Skin
      // follows a continuous throat from the same shoulder to the same jaw.
      const upperChest = g.furLength === "hairless" ? [
        [563,231,469,908,1313],[539,235,468,906,1288],[526,237,467,907,1274],
        [508,239,465,910,1255],[495,240,464,913,1241],[469,243,462,920,1212],
        [456,245,461,923,1199],[439,247,459,927,1183],[419,250,457,931,1167],
        [395,255,454,936,1151],[370,262,451,941,1139],[345,272,449,947,1131]
      ] : [
        [563,231,469,906,1313],[539,241,473,910,1282],[526,221,476,889,1268],[508,222,478,896,1248],
        [495,225,479,899,1226],[469,234,472,909,1180],
        [456,214,496,907,1170],[439,229,486,920,1166],[419,242,477,930,1158],
        [395,250,465,937,1150],[370,253,459,934,1145],[345,272,449,947,1131]
      ];
      const trunk=[
        [917,235,468,1140,1313],[898,202,495,1112,1340],[864,180,512,1085,1360],
        [810,162,526,1064,1376],[758,155,538,1049,1385],[704,171,536,1021,1380],
        [650,197,521,975,1366],[607,211,493,934,1347], ...upperChest
      ];
      function torsoGrowth(h){
        return (.76+.30*Math.sin(Math.PI*clamp(h/2)))*(1-.72*smooth(1.30,2.05,h));
      }
      upright("Body and ruff",trunk,(v,c)=>{
        const h=v.p.y;
        const deposit=torsoGrowth(h);
        v.p.x*=1+fullness*.61*deposit;
        v.p.z+=fullness*.17*c*deposit;
        // The dorsal midline projects onto the outline of the side study.
        // Take its pigment just inside the coat, away from the matte edge.
        if(g.furLength!=="hairless" && c<0) {
          const dorsal=c*c;
          v.sv-=(31+.19*(v.fu-chart.x))*dorsal;
          v.sy=v.fv+(.20*(v.fu-chart.x)-7)*dorsal;
        }
        if(v.fv<650) v.mi=1-smooth(.05,.27,c);
        // On the underside, only the two anatomical limbs carry foot paint.
        if(v.fv>738) v.mi=Math.max(v.mi,smooth(738,819,v.fv));
        if(v.mi>.25 && v.fv>862) {
          v.sy=Math.min(v.fv,862);
          v.sv=clamp(v.sv,1092,1350);
          if(c<0 && g.furLength!=="hairless")v.sy-=64*c*c;
        }
      },24);
      function shoulderCosine(v){
        const row=trunk.findIndex(r=>r[0]<=v.fv);
        const below=trunk[Math.max(0,row-1)], above=trunk[Math.max(0,row)];
        const t=clamp((below[0]-v.fv)/Math.max(1,below[0]-above[0]));
        const front=below[3]+(above[3]-below[3])*t;
        const back=below[4]+(above[4]-below[4])*t;
        return clamp((front+back-2*v.sv)/(back-front),-1,1);
      }
      for(const right of [false,true]) {
        const leg=right ? [
          [958,354,393,991,1042],[949,344,405,980,1047],[933,344,406,979,1049],
          [916,347,393,987,1051],[900,344,385,1003,1053],[839,341,388,1003,1058],
          [774,340,394,998,1059],[712,339,404,985,1063],[651,341,425,968,1077],
          [578,352,460,962,1089],[524,374,464,962,1100],[498,396,450,981,1087]
        ]:[
          [963,280,329,943,986],[954,268,338,931,1001],[939,266,341,929,1007],
          [921,273,338,934,1005],[900,284,335,970,1003],[843,282,336,972,1005],
          [774,272,337,966,1003],[711,262,338,957,1001],[650,250,338,950,991],
          [589,239,342,916,984],[542,243,328,911,985],[499,258,315,931,981]
        ];
        upright("foreleg",leg,(v,c)=>{
          const shoulder=shoulderCosine(v);
          const growth=torsoGrowth(v.p.y)*smooth(.34,1.28,v.p.y);
          v.p.x*=1+fullness*.61*growth;
          v.p.z+=fullness*.17*shoulder*growth;
          // The furred shoulder hides the end of this limb. With an exposed
          // chest, taper just that end under the breast, keeping the elbow fixed.
          if(g.furLength==="hairless") {
            const shoulderTaper=1-.27*smooth(1.12,1.49,v.p.y);
            v.p.x*=shoulderTaper;
            v.p.z*=shoulderTaper;
          }
          // The far foot is cream and the near foot capped; use the matching half.
          v.mi=1-smooth(.12,.75,c);
          if(v.fv<735) v.mi+=(1-smooth(.05,.27,shoulder)-v.mi)*smooth(735,620,v.fv);
        },18);
        const near=right?1:-1;
        const hind=[
          [954,208,264,1168,1298],[939,202,270,1143,1326],[922,206,281,1133,1344],
          [892,183,293,1092,1353],[860,159,300,1070,1374],[818,140,309,1055,1386],
          [780,138,320,1054,1394],[735,153,322,1062,1387],[691,176,311,1090,1377],
          [654,200,308,1130,1350],[621,220,298,1170,1323],[593,244,280,1202,1280]
        ].map(([y,l,r,f,b])=>right?[y,704-r,704-l,f,b]:[y,l,r,f,b]);
        upright("haunch",hind,(v,c)=>{
          const thick=fullness*.56*smooth(.09,.69,v.p.y);
          v.p.x+=near*thick*.42;v.p.z-=thick*.11;
          if(v.fv>869) v.sy=Math.min(v.fv,860);
          // In profile the curled tail covers the hocks. Continue the dark
          // rear haunch across those hidden faces instead of printing a tail
          // stripe on the back of each paw.
          if(g.furLength!=="hairless" && c<0) {
            const back=smooth(806,867,v.fv)*smooth(0,.64,-c);
            v.sy+=(Math.min(v.fv,795)-v.sy)*back;
            v.sv+=(clamp(v.sv,1180,1320)-v.sv)*back;
          }
          if(right && v.fv>892) { v.fu=clamp(v.fu,489,524);v.fv=855; }
        },18);
      }
    } else {
      // Horizontal sections are ordered from the nose towards the tail. The
      // physical contours and the paint studies have separate coordinates.
      const section=(count,fn)=>Array.from({length:count},(_,i)=>{
        const angle=TAU*i/count;
        return fn(Math.sin(angle),Math.cos(angle));
      });
      const round=(n,power)=>Math.sign(n)*Math.pow(Math.abs(n),power);
      const ribcage=[
        // z, width from centre, top, underside, profile-paint x, top, underside
        [1.15,.09,.35,.155,1066,529,593],[1.10,.20,.425,.113,1082,526,604],
        [1.03,.25,.490,.082,1102,522,617],[ .93,.29,.535,.062,1126,524,632],
        [ .83,.32,.56,.045,1151,515,636],[ .70,.36,.56,.036,1190,506,640],
        [ .53,.43,.535,.027,1254,475,645],[ .34,.48,.515,.025,1312,433,646],
        [ .12,.50,.558,.025,1368,391,646],[-.10,.51,.630,.025,1425,363,646],
        [-.30,.51,.678,.026,1497,336,646],[-.49,.49,.670,.033,1564,334,643],
        [-.65,.45,.612,.041,1625,349,640],[-.77,.38,.516,.055,1698,398,636],
        [-.86,.28,.40,.075,1734,437,629],
        [-.92,.15,.28,.11,1755,475,610],[-.94,.055,.21,.14,1770,500,580]
      ];
      const body=ribcage.map(([z,width,top,bottom,sx,st,sb])=>section(28,(s,c)=>{
        const sling=.80+.20*(1-Math.abs(z));
        const x=width*round(s,.87)*(1+fullness*.40*sling);
        const y=bottom+(top*(1+fullness*.24*sling)-bottom)*(.5+.5*round(c,.87));
        const fx=395+width*520*s;
        const fy=clamp(660-y*480,507,646);
        const projectedY=st+(sb-st)*(.5-.5*c);
        // The folded foreleg covers this part of the ribs in the study.
        // Continue the exposed orange coat under the separate limb surface.
        const aboveSleeve=537+Math.min(27,22*Math.pow((sx-1255)/112,2));
        const overlap=smooth(1120,1180,sx)*(1-smooth(1342,1367,sx));
        const sy=projectedY-overlap*Math.max(0,projectedY-aboveSleeve);
        return {p:vec(x,y,z),fu:clamp(fx,155,654),fv:fy,sv:sx,sy,
          mi:Math.max(smooth(.82,.37,z),smooth(520,477,sy))};
      }));
      assemble("Body and ruff",body);
      const forelimb=[
        // z, distance from midline, width, top, underside, front-paint radius,
        // y limits of front study, and profile x for far and near forelegs.
        [1.40,.267,.028,.074,.044,12,609,639,936,1025],
        [1.38,.267,.105,.125,.020,44,584,644,949,1041],
        [1.34,.267,.151,.162,.008,62,576,647,968,1061],
        [1.28,.267,.175,.185,.006,69,575,648,994,1090],
        [1.19,.271,.176,.198,.006,69,572,647,1020,1126],
        [1.10,.278,.160,.194,.012,66,571,646,1053,1160],
        [1.01,.286,.129,.182,.018,59,567,646,1083,1191],
        [ .90,.299,.131,.198,.015,63,554,644,1122,1230],
        [ .76,.321,.146,.224,.012,71,550,641,1163,1271],
        [ .59,.338,.151,.271,.012,78,539,637,1210,1321],
        [ .43,.328,.145,.331,.021,81,525,628,1250,1351],
        [ .32,.303,.125,.379,.044,72,505,614,1289,1364],
        [ .26,.279,.087,.400,.12,54,493,593,1313,1371]
      ];
      const haunch=[
        // A relaxed knee rests beside the belly, the hock folds towards the rump.
        [ .04,.300,.044,.109,.031,1483,594,631],
        [-.04,.330,.099,.155,.014,1493,585,642],
        [-.15,.353,.146,.217,.007,1512,564,645],
        [-.31,.361,.167,.311,.008,1558,491,643],
        [-.48,.353,.171,.383,.014,1613,431,640],
        [-.63,.331,.163,.394,.020,1665,412,634],
        [-.76,.298,.135,.352,.034,1720,440,625],
        [-.84,.259,.080,.273,.055,1761,479,614],
        [-.86,.237,.045,.216,.108,1786,517,595]
      ];
      for(const flank of [-1,1]){
        const paw=forelimb.map(([z,span,width,top,bottom,paintRadius,ft,fb,far,near])=>
          section(26,(s,c)=>{
            // The toe bank forms one paw with three shallow knuckle ridges.
            const toe=smooth(1.04,1.29,z);
            const ridges=.5+.5*Math.cos(s*Math.PI*3.35);
            const knuckle=toe*ridges*Math.max(c,0);
            const x=flank*(span+fullness*.036*smooth(1.00,.43,z))+
              width*round(s,.88)*(1+.13*fullness);
            const arch=top*(1+.10*smooth(.93,1.18,z))*(1+.11*fullness);
            const y=bottom+(arch-bottom)*(.5+.5*round(c,.88))+knuckle*.012;
            const reach=z-.063*smooth(.72,1.36,z)+toe*(.013*ridges+(flank<0?.035:0));
            const frontX=(flank<0?285:493)+s*paintRadius*.84;
            const fy=ft+(fb-ft)*(.5-.5*c)+9;
            const sx=near+(far-near)*(flank<0?smooth(1.08,1.25,z):0);
            const mix=smooth(1.34,1.19,z);
            const toeY=clamp(Math.max(fy,594+24*s*s),594,650);
            const profileY=fy+Math.max(0,591-fy)*smooth(1.03,1.16,z);
            return {p:vec(x,y,reach),fu:frontX,fv:toeY,sv:sx,sy:profileY,mi:mix};
          }));
        assemble("foreleg",paw);
        const thigh=haunch.map(([z,span,width,top,bottom,sx,st,sb])=>section(22,(s,c)=>{
          const x=flank*span*(1+.24*fullness)+width*s*(1+.25*fullness);
          const y=bottom+(top*(1+.24*fullness)-bottom)*(.5+.5*round(c,.90));
          const sy=st+(sb-st)*(.5-.5*c);
          return {p:vec(x,y,z),fu:clamp(395+x*440,144,646),fv:clamp(sy,514,651),
            sv:sx,sy,mi:1};
        }));
        assemble("haunch",thigh);
      }
    }
    if(g.furLength==="hairless") {
      // The uncovered tail leaves the sacrum on the midline. The seated
      // adult wraps it around the hock; the kitten rests it behind the hip.
      const spine=baby ? [
        [0,.321,-.805],[.055,.270,-.955],[.148,.166,-1.081],
        [.277,.086,-1.209],[.427,.054,-1.380],[.502,.051,-1.540],
        [.501,.069,-1.681],[.424,.094,-1.793],[.320,.109,-1.848]
      ] : [
        [0,.828,-.862],[.058,.700,-1.025],[.233,.494,-1.097],
        [.490,.356,-1.033],[.651,.269,-.919],[.755,.195,-.729],
        [.801,.133,-.533],[.795,.092,-.346],[.748,.067,-.163],
        [.659,.051,.006],[.519,.046,.159],[.377,.047,.271],
        [.262,.066,.334],[.215,.087,.353]
      ];
      const curve=new T.CatmullRomCurve3(spine.map(([x,y,z],i)=>{
        const t=i/(spine.length-1);
        if(baby) y+=.061*fullness*(1-smooth(0,.52,t));
        else {
          x+=.13*fullness*smooth(0,.42,t)*(1-smooth(.68,1,t));
          z-=.16*fullness*(1-smooth(0,.48,t));
        }
        return vec(x,y,z);
      }),false,"centripetal");
      const centers=curve.getSpacedPoints(60);
      const rings=centers.map((n,i)=>{
        const t=i/(centers.length-1);
        const tangent=centers[Math.min(i+1,centers.length-1)].clone().sub(centers[Math.max(i-1,0)]).normalize();
        const side=vec(0,1,0).cross(tangent).normalize(), up=tangent.clone().cross(side);
        // Exposed vertebrae taper over the full length; the terminal ring
        // closes into a small round tip. Cross sections near the floor remain oval.
        const radius=(baby?.066:.098)*(1+.10*fullness)*(1-.79*t)*
          Math.sqrt(Math.max(.002,1-Math.pow(smooth(.975,1,t),2)));
        return Array.from({length:16},(_,j)=>{
          const a=TAU*j/16;
          const p=n.clone().addScaledVector(side,radius*Math.cos(a))
            .addScaledVector(up,Math.min(radius,n.y-.006)*Math.sin(a));
          const fx=chart.x+p.x*chart.xyUnit, fy=chart.floor-p.y*chart.xyUnit, sx=chart.z-p.z*chart.zUnit;
          return {p,fu:fx,fv:fy,sv:sx,mi:.5};
        });
      });
      assemble("tail",rings);
      blendSkinContact(T,root.getObjectByName("tail").geometry,root.getObjectByName("Body and ruff").geometry,
        centers[0],baby?.37:.49,baby?.092:.135);
      return;
    }
    // The painted tail curls in front of the seated haunch but trails behind
    // the prone kitten. Its own tube has a continuous tip on every rotation.
    const tail=baby?[
      [566,507,1743,39],[602,556,1823,49],[624,599,1903,42],
      [638,594,1983,44],[631,592,2030,47],[619,606,2084,32],[598,631,2115,7]
    ]:[
      [476,799,1340,29],[505,830,1374,40],[532,856,1377,42],[546,881,1342,43],
      [538,902,1297,45],[508,916,1235,42],[478,917,1185,44],[442,919,1141,42],
      [421,927,1096,34],[410,938,1066,23],[407,947,1052,9]
    ];
    const nodes=tail.map(([fx,y,sx])=>point(fx,y,sx));
    const tailRings=nodes.map((n,i)=>{
      const previous=nodes[Math.max(0,i-1)],next=nodes[Math.min(nodes.length-1,i+1)];
      const tangent=next.clone().sub(previous).normalize();
      const right=vec(0,1,0).cross(tangent).normalize(), up=tangent.clone().cross(right).normalize();
      const radius=tail[i][3]/chart.xyUnit;
      return Array.from({length:16},(_,k)=>{
        const a=TAU*k/16;
        const p=n.clone().addScaledVector(right,radius*Math.cos(a)).addScaledVector(up,radius*Math.sin(a));
        p.y=Math.max(.008,p.y);
        const fx=chart.x+p.x*chart.xyUnit, fy=chart.floor-p.y*chart.xyUnit, sx=chart.z-p.z*chart.zUnit;
        return {p, fu:baby?clamp(fx,529,627):clamp(fx,406,561),fv:baby?clamp(fy,516,629):clamp(fy,841,951),
          sv:baby?clamp(sx-53,1687,2061):clamp(sx,1048,1392),
          sy:baby?clamp(fy+7,555+60*smooth(2033,2113,sx),644):clamp(fy,844,953),
          mi:baby?1:.46};
      });
    });
    assemble("tail",tailRings);
  }
  window.WoodBody={build,blendSkinContact};
})();
