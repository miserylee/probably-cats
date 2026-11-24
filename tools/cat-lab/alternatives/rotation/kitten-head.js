/* The juvenile skull is a separate carving. Its facial planes, ears and
   reference projections all share coordinates in the juvenile studies. */
(() => {
  "use strict";
  const clamp=(n,a=0,b=1)=>Math.max(a,Math.min(b,n));
  const mix=(a,b,t)=>a+(b-a)*t;
  function smooth(a,b,n) {
    const t=clamp((n-a)/(b-a));
    return t*t*(3-2*t);
  }
  function fit(n,knots) {
    let i=1;
    while(i<knots.length-1 && n>knots[i][0]) i++;
    const [a,b]=knots[i-1], [c,d]=knots[i];
    return mix(b,d,clamp((n-a)/(c-a)));
  }
  function bell(n){return Math.exp(-n*n);}

  // Horizontal coordinates in each drawing and their common vertical axis.
  // Forward depth is positive, so a nose facing left has decreasing profile U.
  const sheet={w:2172,h:724,mid:395,half:235,chin:509,crown:192,nose:886,occiput:1304};
  const height=py=>(sheet.chin-py)/(sheet.chin-sheet.crown);
  const width=px=>(px-sheet.mid)/sheet.half;
  const depth=px=>(sheet.occiput-px)/(sheet.occiput-sheet.nose);
  const profileRows=[[112,116],[142,130],[192,197],[220,219],[270,262],[315,303],
    [350,340],[374,359],[393,374],[407,388],[430,411],[455,448],[485,480],[509,497],[525,513]];
  // Chin to crown slices from both paintings, passing through each orbit.
  const slices=[
    [508,381,402,1007,1015,1073,1104], [503,333,453,981,997,1099,1165],
    [493,290,499,946,976,1122,1210], [477,244,536,926,965,1139,1255],
    [460,211,563,914,956,1148,1271], [447,196,577,902,946,1147,1281],
    [428,178,596,891,946,1145,1292], [410,167,606,888,949,1147,1301],
    [395,159,615,893,966,1151,1303], [384,158,617,916,977,1154,1304],
    [378,160,616,927,982,1155,1304], [370,165,612,937,988,1157,1304],
    [357,175,603,943,997,1161,1304], [347,182,596,946,1007,1162,1303],
    [333,188,589,949,1014,1163,1302], [317,194,582,951,1019,1164,1297],
    [297,206,570,957,1016,1160,1290], [270,220,553,969,1016,1151,1281],
    [246,244,531,987,1021,1143,1261], [226,267,510,1008,1032,1138,1244],
    [212,289,493,1022,1045,1135,1226], [201,318,467,1040,1063,1132,1208],
    [195,348,433,1064,1078,1128,1181], [192,381,400,1112,1114,1123,1132]
  ].map(([py,l,r,f,o,t,b])=>[height(py),width(l),width(r),depth(f),depth(o),depth(t),depth(b)]);
  const eyeCenters=[[width(314),height(353)],[width(470),height(351)]];
  const noseHeight=height(408);
  const earOutlines=[
    [[194,329,1263],[180,303,1245],[169,258,1225],[167,230,1203],[171,191,1182],
      [179,158,1156],[189,144,1142],[199,141,1131],[210,143,1127],[236,151,1120],
      [283,174,1104],[327,201,1083],[302,224,1109],[247,274,1181],[214,315,1227]],
    [[433,208,1103],[451,196,1113],[488,172,1134],[539,148,1154],[562,141,1163],
      [574,141,1172],[584,150,1185],[593,167,1199],[599,190,1214],[603,226,1234],
      [601,259,1251],[595,290,1269],[584,327,1288],[553,288,1243],[512,252,1194],[466,231,1134]]
  ].map(loop=>loop.map(([x,y,z])=>[width(x),height(y),depth(z)]));

  function build(T,root,genes,kit) {
    const head=new T.Group();
    head.name="Kitten forehead, cheeks and ears";
    root.add(head);
    const g=key=>(genes[key]??.5)-.5;
    const eyeGene=g("eyes"), earGene=g("ears"), cheekGene=g("cheeks");
    const muzzleGene=g("muzzle"), condition=g("fullness");
    const positions=[],frontUV=[],sideUV=[],registeredUV=[],viewMix=[],profile=[],ginger=[],indices=[];
    const fromFront=(x,h)=>[(sheet.mid+x*sheet.half)/sheet.w,1-(sheet.chin+(sheet.crown-sheet.chin)*h)/sheet.h];
    const fromSide=(z,h)=>[(sheet.occiput+(sheet.nose-sheet.occiput)*z)/sheet.w,1-fit(sheet.chin+(sheet.crown-sheet.chin)*h,profileRows)/sheet.h];
    // The orange ear turns slightly toward the viewer. Its projection shifts
    // together with the adjoining temple all the way around the ear root.
    function earSide(sign,z,h) {
      const uv=fromSide(z,h);
      if(sign<0) {
        const px=uv[0]*sheet.w;
        uv[0]+=27/sheet.w*smooth(.45,.78,h)*smooth(1055,1140,px)*(1-smooth(1240,1290,px));
      }
      return uv;
    }
    // Match the two sheets at the temple where the frontal cheek ends.
    const eyeMapX=[[433,986],[439,993],[445,997],[451,1003],[460,1010],[466,1015],
      [472,1020],[478,1026],[487,1037],[496,1050],[502,1062],[510,1070],[527,1085]];
    const eyeTop=[[433,372,378],[439,347,367],[445,334,320],[451,326,312],[460,319,308],
      [466,316,308],[472,314,308],[478,313,309],[487,313,314],[496,314,322],[502,315,331],[510,318,334],[527,323,336]];
    const eyeBottom=[[433,378,380],[439,377,378],[445,378,379],[451,379,378],[460,380,376],
      [466,379,375],[472,378,374],[478,376,372],[487,370,367],[496,360,360],[502,348,350],[510,331,344],[527,325,339]];
    const whiskerTop=[[449,422,412],[460,421,411],[475,419,413],[485,419,416],[500,419,422],
      [515,419,429],[525,422,434],[535,424,438],[550,429,444],[558,433,447],[575,442,454]];
    const whiskerBottom=[[449,432,425],[460,433,424],[475,437,429],[485,439,437],
      [500,447,451],[515,457,464],[525,467,470],[535,472,476],[545,476,482],[555,488,491],[575,510,510]];
    const marks=[[395,394,891,376],[381,396,887,377],[415,405,918,388],[395,425,891,411],
      [396,446,893,434],[416,456,922,450],[447,444,958,438],[444,421,949,413],
      [469,419,973,415],[504,418,1008,425],[540,424,1038,438],[572,438,1066,453],
      [446,430,949,425],[467,436,968,433],[489,445,990,447],[512,462,1010,461],
      [539,486,1031,482],[437,283,994,268],[476,273,1023,261],[510,288,1057,281],
      [485,241,1065,226],[560,329,1131,320],[593,394,1184,379]].map(([x,y,sx,sy])=>{
        const h=height(y), w=width(x);
        let i=1;
        while(i<slices.length-1&&slices[i][0]<h)i++;
        const a=slices[i-1],b=slices[i],f=clamp((h-a[0])/(b[0]-a[0]));
        const row=a.map((v,j)=>mix(v,b[j],f));
        const t=clamp(w/row[2]),z=surface(t,row[3],row[4],row[5])+browAndMuzzle(w,h);
        const uv=fromSide(z,h);
        return [x,y,sx-uv[0]*sheet.w,sy-(1-uv[1])*sheet.h];
      });
    const rowCurve=(x,rows,col)=>fit(x,rows.map(r=>[r[0],r[col]]));
    function registeredSide(x,h,base) {
      const fx=sheet.mid+x*sheet.half,fy=sheet.chin+(sheet.crown-sheet.chin)*h;
      let sum=0,sx=0,sy=0;
      for(const [mx,my,u,v] of marks) {
        const d2=(fx-mx)**2+(fy-my)**2,w=1/(d2+30)**1.72;
        sum+=w;sx+=u*w;sy+=v*w;
      }
      let u=base[0]*sheet.w+sx/(sum+.000001);
      let v=(1-base[1])*sheet.h+sy/(sum+.000001);
      const weight=smooth(418,436,fx)*(1-smooth(529,547,fx))*smooth(289,309,fy)*(1-smooth(393,404,fy));
      if(weight>0) {
        const ft=rowCurve(fx,eyeTop,1),fb=rowCurve(fx,eyeBottom,1);
        const st=rowCurve(fx,eyeTop,2),sb=rowCurve(fx,eyeBottom,2);
        const middle=(ft+fb)*.5;
        const scale=clamp((sb-st)/Math.max(8,fb-ft),.55,1.45);
        u=mix(u,fit(fx,eyeMapX),weight);v=mix(v,(st+sb)*.5+(fy-middle)*scale,weight);
      }
      const pad=smooth(438,451,fx)*(1-smooth(576,596,fx))*smooth(398,414,fy)*(1-smooth(487,505,fy));
      if(pad>0) {
        const top=rowCurve(fx,whiskerTop,1),bottom=rowCurve(fx,whiskerBottom,1);
        const d0=rowCurve(fx,whiskerTop,2)-top,d1=rowCurve(fx,whiskerBottom,2)-bottom;
        u=mix(u,fx+500,pad);
        v=mix(v,fy+mix(d0,d1,clamp((fy-top)/(bottom-top))),pad);
      }
      return [u/sheet.w,1-v/sheet.h];
    }
    // Two small sweeps of cheek fur turn out across the jaw. The folds run
    // diagonally into the temple and end before reaching the whisker pads.
    const cheekEdge=[[.12,0],[.21,-.10],[.255,.16],[.304,.53],[.333,.02],
      [.36,.15],[.414,.91],[.442,-.10],[.50,-.025],[.61,0]];
    function vertex(x,h,z,front,side,blend,ear=false,reach=0,registered=side,reverse=0) {
      frontUV.push(...front);sideUV.push(...side);registeredUV.push(...registered);viewMix.push(blend);profile.push(reach);
      ginger.push(reverse);
      if(ear) {
        const above=smooth(.67,1.16,h);
        const apex=Math.sign(x)*(x<0?.83:.76);
        h+=.029*smooth(1.02,1.16,h)*(1-smooth(.01,.13,Math.abs(x-apex)));
        h+=earGene*.18*above;
        x+=Math.sign(x)*earGene*.055*above;
      } else {
        const rawX=x;
        const breadth=Math.abs(x), side=Math.sign(x);
        if(genes.furLength!=="hairless") {
          const outer=smooth(.57,.95,breadth)*smooth(.18,.40,z)*(1-smooth(.62,.83,z));
          const fold=fit(h+.13*(1-smooth(.52,1,breadth))+.18*(z-.43)+.012*side,cheekEdge);
          x+=side*outer*(.116*fold-.07*(1-smooth(.14,.34,h)));
          z-=.046*Math.max(0,fold)*outer;
          h-=.011*Math.max(0,fold)*outer;
        }
        const centre=1-smooth(.06,.19,breadth);
        z+=centre*(.021*bell((h-.51)/.19)+.025*bell((h-noseHeight)/.050))*smooth(.65,.88,z);
        for(const [ex,eh] of eyeCenters) {
          const dx=rawX-ex, dh=h-eh;
          const socket=bell(dx/.24)*bell(dh/.16)*smooth(.50,.78,z);
          x+=eyeGene*dx*.41*socket;
          h+=eyeGene*dh*.81*socket;
        }
        const pad=bell((h-.30)/.27)*smooth(.27,.94,Math.abs(rawX));
        x+=Math.sign(rawX)*(.15*cheekGene+.07*condition)*pad;
        const snout=bell((h-noseHeight)/.19)*bell(rawX/.39)*smooth(.53,.82,z);
        z+=muzzleGene*.17*snout;
      }
      positions.push(x*.511,.315+h*.661,.41+z*.92);
      return positions.length/3-1;
    }
    function face(a,b,c,axis,sign) {
      const p=3*a,q=3*b,r=3*c;
      const u=[positions[q]-positions[p],positions[q+1]-positions[p+1],positions[q+2]-positions[p+2]];
      const v=[positions[r]-positions[p],positions[r+1]-positions[p+1],positions[r+2]-positions[p+2]];
      const n=u[(axis+1)%3]*v[(axis+2)%3]-u[(axis+2)%3]*v[(axis+1)%3];
      if(n*sign<0)indices.push(a,c,b);else indices.push(a,b,c);
    }
    function quad(a,b,c,d,axis,sign){face(a,b,c,axis,sign);face(a,c,d,axis,sign);}
    function browAndMuzzle(x,h) {
      let z=0;
      for(const [ex,eh] of eyeCenters) {
        const dx=x-ex,dy=h-eh-.13*dx*Math.sign(ex);
        const r=Math.hypot(dx/.214,dy/.119);
        z+=.031*bell(r*1.25)-.014*bell((r-1.02)/.23);
        z+=.014*bell(dx/.19)*bell((dy-.14)/.055);
      }
      for(const side of [-1,1]) z+=.068*bell((x-side*.18)/.16)*bell((h-noseHeight+.075)/.09);
      return z;
    }
    function surface(t,ridge,orbit,temple) {
      return fit(t,[[0,ridge],[.07,mix(ridge,orbit,.065)],[.19,mix(ridge,orbit,.43)],[.37,orbit],[.67,mix(orbit,temple,.38)],
        [.84,mix(orbit,temple,.56)],[.94,mix(orbit,temple,.76)],[1,temple]]);
    }
    const rows=[];
    slices.forEach((row,i)=>{
      rows.push(row);
      if(i<slices.length-1 && row[0]>.04 && row[0]<.86) {
        for(const t of [.33,.67])rows.push(row.map((v,j)=>mix(v,slices[i+1][j],t)));
      }
    });
    const steps=22;
    const leftToRight=[[155,621],[206,574],[245,540],[258,524],[270,510],[279,504],
      [291,497],[299,489],[306,478],[315,470],[324,463],[333,455],[342,446],
      [351,439],[358,433],[371,417],[395,395]];
    const leftLid=[[253,341,343],[259,340,344],[270,335,347],[279,330,359],[291,325,371],
      [299,322,375],[306,321,377],[315,320,379],[324,319,379],[333,322,377],
      [342,332,373],[351,349,371],[358,369,374]];
    const leftWhiskers=[[225,456,495],[235,449,490],[245,441,481],[255,435,474],[270,430,465],
      [280,427,458],[295,425,449],[305,422,445],[315,421,440],[325,419,436],[340,420,431]];
    function orangeLandmarks(x,h) {
      const fx=sheet.mid+x*sheet.half,py=sheet.chin+(sheet.crown-sheet.chin)*h;
      const other=fit(fx,leftToRight);
      let line=py;
      for(const [contours,top,bottom,weight] of [
        [leftLid,eyeTop,eyeBottom,smooth(245,256,fx)*(1-smooth(358,370,fx))*smooth(297,318,py)*(1-smooth(384,402,py))],
        [leftWhiskers,whiskerTop,whiskerBottom,smooth(209,226,fx)*(1-smooth(340,351,fx))*smooth(398,417,py)*(1-smooth(484,500,py))]
      ]) {
        if(weight===0)continue;
        const a=rowCurve(fx,contours,1),b=rowCurve(fx,contours,2);
        const c=rowCurve(other,top,1),d=rowCurve(other,bottom,1);
        const t=clamp((py-a)/Math.max(1,b-a),-.25,1.25);
        line=mix(line,py+mix(c-a,d-b,t),weight);
      }
      return [width(other),height(line)];
    }
    const cheekSeams=[];
    const facePaint=window.CatFaceProjection;
    for(const sign of [-1,1]) {
      const skin=[],back=[];
      for(const [h,lo,hi,ridge,orbit,temple,occiput] of rows) {
        const edge=sign<0?lo:hi;
        const frontRing=[];
        let edgeMap;
        for(let j=0;j<=steps;j++) {
          // A frontal cheek ends on a rounded plane. A narrow muzzle makes a
          // distinct small projection below the much wider orbit and forehead.
          const a=j/steps*Math.PI/2, t=Math.sin(a), x=edge*t;
          const z=surface(t,ridge,orbit,temple)+browAndMuzzle(x,h);
          const blend=smooth(.81,1,t);
          const side=earSide(sign,z,h);
          const front=fromFront(x,h);
          let pigment=front;
          if(blend>0) {
            const matched=sign>0?[x,h]:orangeLandmarks(x,h);
            pigment=registeredSide(matched[0],matched[1],side);
          }
          frontRing.push(vertex(x,h,z,front,pigment,blend,false,0,pigment,Number(sign<0)));
          if(j===steps)edgeMap=pigment;
        }
        skin.push(frontRing);
        const nape=[];
        for(let k=0;k<=10;k++) {
          const a=k/10*Math.PI/2;
          const z=k===0 ? temple+browAndMuzzle(edge,h) : mix(temple,occiput,Math.sin(a));
          const mastoid=smooth(.35,.68,h)*(1-smooth(.80,1,h));
          const x=edge*(Math.cos(a)+.30*Math.pow(Math.sin(a*2),1.2)*mastoid);
          const crown=facePaint.crown(x,1-h,true);
          const edgeWeight=1-facePaint.napeBlend(1-h,Math.sin(a));
          nape.push(vertex(x,h,z,crown,edgeMap,edgeWeight,false,0,edgeMap,Number(sign<0)));
        }
        cheekSeams.push([frontRing[steps],nape[0]]);
        back.push(nape);
      }
      for(let i=1;i<rows.length;i++) {
        // Winding follows the shell's topology. The swept tips can point
        // backward, so their winding must survive a turn through 90 degrees.
        for(const strip of [skin,back]) for(let j=1;j<strip[i].length;j++) {
          const a=strip[i-1][j-1],b=strip[i-1][j],c=strip[i][j],d=strip[i][j-1];
          if(sign>0)indices.push(a,b,c,a,c,d);
          else indices.push(a,c,b,a,d,c);
        }
      }
      for(const [i,up] of [[0,-1],[rows.length-1,1]]) {
        const [h,,,,,temple]=rows[i], middle=vertex(0,h,temple,fromFront(0,h),fromSide(temple,h),.1);
        const loop=[...skin[i],...back[i].slice(1)];
        for(let k=1;k<loop.length;k++)face(middle,loop[k-1],loop[k],1,up);
      }
    }
    const pinnae=[];
    for(const outline of earOutlines) {
      const earStart=indices.length;
      const sign=Math.sign(outline[0][0]);
      const center=outline.reduce((p,v)=>p.map((n,i)=>n+v[i]/outline.length),[0,0,0]);
      function earPoint(p,dz,back=false) {
        const [x,h,z]=p;
        const w=back ? 1 : 0;
        let s=back?[(1213+30*clamp(h)+21*Math.abs(x))/sheet.w,
          1-(242+44*(1-clamp(h)))/sheet.h]:fromSide(z+dz,h);
        if(back && (genes.earShape==="slight" || genes.earShape==="folded")) {
          // Use plum temple fur on the dorsal pinna, with the same ginger
          // conversion as the left cheek. Both ears keep a painted grain.
          const px=432+46*Math.abs(x)+10*h,py=234+16*(1-h)+10*(1-Math.abs(x));
          s=[px/sheet.w,1-py/sheet.h];
        }
        const earProfile=back?s:earSide(sign,z+dz,h);
        return vertex(x,h,z+dz,fromFront(x,h),earProfile,w,true,0,earProfile,Number(sign<0));
      }
      const rim=outline.map(p=>earPoint(p,0));
      const inside=outline.map(p=>earPoint(p.map((a,i)=>mix(a,center[i],.20)),-.026));
      const backRim=outline.map(p=>earPoint(p,-.034,true));
      const cup=earPoint(center,-.08),rear=earPoint([center[0],center[1]-.025,center[2]],-.16,true);
      for(let i=0;i<rim.length;i++) {
        const j=(i+1)%rim.length;
        quad(rim[i],rim[j],inside[j],inside[i],2,1);face(inside[i],inside[j],cup,2,1);
        quad(rim[i],backRim[i],backRim[j],rim[j],0,sign);
        face(backRim[j],backRim[i],rear,2,-1);
      }
      // The sides of a real ear continue below the visible opening. Sink the
      // root into the temple, with the outer base curving toward the occiput.
      const base=sign>0?[9,10,11,12,13,14,15,0,1]:[10,11,12,13,14,0,1,2,3];
      const inserted=base.map(i=>{
        const [x,h,z]=outline[i], u=.56*x, vh=Math.min(h-.16,.54), vz=mix(z,.40,.71);
        const s=earSide(sign,vz,vh);
        return vertex(u,vh,vz,fromFront(u,vh),s,.72,true,0,s,Number(sign<0));
      });
      for(let i=1;i<base.length;i++){
        const j=base[i-1],k=base[i];
        quad(rim[j],rim[k],inserted[i],inserted[i-1],2,1);
        face(backRim[j],inserted[i-1],inserted[i],2,-1);
        face(backRim[j],inserted[i],backRim[k],2,-1);
      }
      pinnae.push({start:earStart,end:indices.length,side:sign,steps:5});
    }
    const geom=new T.BufferGeometry();
    geom.setAttribute("position",new T.Float32BufferAttribute(positions,3));
    geom.setAttribute("frontUV",new T.Float32BufferAttribute(frontUV,2));
    geom.setAttribute("sideUV",new T.Float32BufferAttribute(sideUV,2));
    geom.setAttribute("registeredSideUV",new T.Float32BufferAttribute(registeredUV,2));
    geom.setAttribute("profileReach",new T.Float32BufferAttribute(profile,1));
    geom.setAttribute("mirroredGinger",new T.Float32BufferAttribute(ginger,1));
    geom.setAttribute("viewMix",new T.Float32BufferAttribute(viewMix,1));
    geom.setIndex(indices);geom.computeVertexNormals();
    const normal=geom.getAttribute("normal");
    for(const [front,back] of cheekSeams) {
      const both=new T.Vector3().fromBufferAttribute(normal,front)
        .add(new T.Vector3().fromBufferAttribute(normal,back)).normalize();
      normal.setXYZ(front,both.x,both.y,both.z);
      normal.setXYZ(back,both.x,both.y,both.z);
    }
    const seams=new Map();
    for(let i=0;i<positions.length;i+=3) {
      if(Math.abs(positions[i])>1e-8)continue;
      const key=positions[i+1].toFixed(5)+"/"+positions[i+2].toFixed(5);
      const seam=seams.get(key)??[];seam.push(i/3);seams.set(key,seam);
    }
    for(const seam of seams.values()) {
      const n=new T.Vector3();
      for(const i of seam)n.add(new T.Vector3().fromBufferAttribute(normal,i));
      n.normalize();
      for(const i of seam)normal.setXYZ(i,n.x,n.y,n.z);
    }
    // Keep broad planes visible under the same light as the adult carving.
    // Retain part of the averaged normal where a plane rolls into the next.
    const carving=geom.toNonIndexed(),averaged=carving.getAttribute("normal").clone();
    carving.userData.pinnae=pinnae;
    carving.computeVertexNormals();
    const planes=carving.getAttribute("normal"),n=new T.Vector3(),nearby=new T.Vector3();
    for(let i=0;i<planes.count;i++) {
      n.fromBufferAttribute(planes,i);
      nearby.fromBufferAttribute(averaged,i);
      n.lerp(nearby,.40).normalize();
      planes.setXYZ(i,n.x,n.y,n.z);
    }
    geom.dispose();carving.computeBoundingSphere();
    head.add(new T.Mesh(carving,kit.material));
    return head;
  }
  function blendThroat(T,root,head) {
    // Only the underside resting on the raised breast shares its shading.
    // The projecting chin and cheek edges retain their own silhouette.
    head.updateMatrix();
    const influence=p=>(1-smooth(.50,.63,p.y))*(1-smooth(1.09,1.24,p.z));
    window.WoodBody.blendSkinContact(T,head.children[0].geometry,root.getObjectByName("Body and ruff").geometry,
      new T.Vector3(0,.40,.91),.58,.12,head.matrix,influence);
  }
  window.KittenHead={build,blendThroat};
})();
