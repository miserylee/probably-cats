/* Planes for a fixed-pose, black-mask shorthair. +Z is the muzzle. */
(() => {
  "use strict";
  function build(T, root, genes, colors) {
    const clamp=(x,a=0,b=1)=>Math.min(b,Math.max(a,x));
    const lerp=(a,b,t)=>a+(b-a)*t;
    const mix=(a,b,t)=>a.clone().lerp(b,t);
    const make=s=>new T.Color(s);
    const gene=(name,fallback)=>Number.isFinite(genes[name])?clamp(genes[name]):fallback;
    const kitten=genes.age==="kitten", cheek=gene("cheeks",.4), weight=gene("fullness",.45);
    const head=new T.Group(); root.add(head);
    head.position.set(0,kitten?.51:1.99,kitten?.89:.30);
    head.rotation.set(-.043,kitten?-.51:.56,kitten?.035:-.060);
    const size=kitten?.68:1;
    head.scale.set(.94*size,size,size);
    const dark=colors.dark, cream=colors.light, ink=colors.ink, warm=colors.warm;
    const mat=new T.MeshStandardMaterial({vertexColors:true,roughness:.93,metalness:0});
    const shell=[], paint=[], eyes=[], whiskers=[], ears=[], nose=[];
    function emit(batch, list, color, axis='z', orient=1) {
      for(let k=1;k<list.length-1;k++) {
        let a=list[0], b=list[k], c=list[k+1];
        const n=new T.Vector3().crossVectors(new T.Vector3().fromArray(b).sub(new T.Vector3().fromArray(a)),
          new T.Vector3().fromArray(c).sub(new T.Vector3().fromArray(a)));
        if(n.lengthSq()<1e-14) continue;
        if(axis && n[axis]*orient<0) [b,c]=[c,b];
        batch.push([a,b,c,color]);
      }
    }
    function draw(batch,name,coatOnly=false,shiny=false) {
      const pos=[],rgb=[];
      for(const [a,b,c,col] of batch) for(const p of [a,b,c]) {
        pos.push(...p);
        const tint=typeof col==='function'?col(...p):col;
        rgb.push(tint.r,tint.g,tint.b);
      }
      const g=new T.BufferGeometry();
      g.setAttribute('position',new T.Float32BufferAttribute(pos,3));
      g.setAttribute('color',new T.Float32BufferAttribute(rgb,3)); g.computeVertexNormals();
      const m=new T.Mesh(g,shiny?new T.MeshStandardMaterial({vertexColors:true,roughness:.48}):mat);
      m.name=name; m.userData.coatOnly=coatOnly; m.userData.skipCastShadow=coatOnly;
      head.add(m); return m;
    }
    // The inner columns describe the forehead, bridge, whisker cushions and
    // chin. Outer columns turn back to form the zygomatic arch and temples.
    const muzzle=gene("muzzle",.44);
    const rows=[
      // half-width, heights across centre > outside, depth across the same row
      [.29,[.466,.465,.453,.438,.40],[.021,.019,.010,-.012,-.078]],
      [.47,[.396,.397,.388,.363,.316],[.258,.253,.227,.153,-.033]],
      [.57,[.245,.262,.260,.244,.203],[.409,.413,.399,.330,.035]],
      [.59,[.094,.115,.136,.125,.093],[.473,.458,.420,.348,.062]],
      [.60,[.010,.029,.039,.022,-.025],[.526,.473,.412,.341,.058]],
      [.580,[-.106,-.083,-.091,-.078,-.105],[.618,.536,.441,.347,.028]],
      [.529,[-.188,-.181,-.180,-.155,-.190],[.611,.602,.484,.312,-.009]],
      [.421,[-.283,-.272,-.255,-.249,-.273],[.517,.526,.446,.278,-.024]],
      [.298,[-.399,-.376,-.348,-.335,-.342],[.387,.396,.350,.226,-.052]],
      [.13,[-.460,-.451,-.432,-.412,-.395],[.214,.211,.189,.128,-.080]]
    ];
    const across=[0,.245,.495,.782,1];
    const faces=[], hull=[];
    for(const sign of [-1,1]) {
      const points=rows.map(([span,ys,zs],i)=>across.map((s,k)=>{
        const arch=(i>=3 && i<=7)?(.018+.092*cheek+.027*weight):.018;
        const depth=(.020+.12*muzzle)*Math.exp(-((i-5.55)**2)/2.15)*(1-s**1.45);
        return [sign*(span+arch)*s,ys[k],zs[k]+depth];
      }));
      for(let j=0;j<points.length-1;j++) for(let k=0;k<4;k++){
        const a=points[j][k], b=points[j][k+1], c=points[j+1][k],d=points[j+1][k+1];
        const tone=mix(dark,warm,.055+.13*(1-k/4)*Math.max(0,1-j/10));
        if ((j===5 || j===6) && k===2) {emit(faces,[a,b,c],tone);emit(faces,[b,d,c],tone);}
        else {emit(faces,[a,b,d],tone);emit(faces,[a,d,c],tone);}
      }
      const side=points.map(row=>row[4]);
      const back=side.map(([x,y],i)=>[x*.85,y*.97+.014,-.182-(.155+.065*weight)*Math.sin(Math.PI*i/(side.length-1))]);
      const spine=back.map(([x,y,z],i)=>[0,y*.97+.014,z-.095-.075*Math.sin(Math.PI*i/(side.length-1))]);
      for(let j=0;j<side.length-1;j++) {
        emit(hull,[side[j],side[j+1],back[j+1],back[j]],dark,'x',sign);
        emit(hull,[back[j],back[j+1],spine[j+1],spine[j]],dark,'z',-1);
      }
      for(const row of [0,points.length-1]) {
        const axis=row===0?1:-1;
        const center=spine[row];
        emit(hull,[center,back[row],side[row]],dark,'y',axis);
        for(let k=0;k<4;k++) emit(hull,[center,points[row][k],points[row][k+1]],dark,'y',axis);
      }
    }
    shell.push(...faces,...hull);
    draw(shell,'Mask and cheekbones');

    function cross(a,b,x,y){return (b[0]-a[0])*(y-a[1])-(b[1]-a[1])*(x-a[0]);}
    function depthAt(x,y) {
      let z=-1;
      for(const [a,b,c] of faces){
        const det=cross(a,b,c[0],c[1]);
        const u=cross(a,b,x,y)/det, v=cross(b,c,x,y)/det, w=1-u-v;
        if(Math.min(u,v,w)>=-.0001) z=Math.max(z,u*c[2]+v*a[2]+w*b[2]);
      }
      return z;
    }
    function patch(batch,poly,color,lift=.0012) {
      for(const [a,b,c] of faces){
        let pts=poly;
        for(const [s,e] of [[a,b],[b,c],[c,a]]) {
          const clipped=[];
          for(let i=0;i<pts.length;i++) {
            const p=pts[i],q=pts[(i+1)%pts.length], dp=cross(s,e,...p),dq=cross(s,e,...q);
            if(dp>=0)clipped.push(p);
            if((dp>=0)!==(dq>=0)) {
              const t=dp/(dp-dq);clipped.push([lerp(p[0],q[0],t),lerp(p[1],q[1],t)]);
            }
          }
          pts=clipped; if(pts.length<3)break;
        }
        if(pts.length<3)continue;
        const det=cross(a,b,c[0],c[1]);
        emit(batch,pts.map(([x,y])=>{
          const u=cross(a,b,x,y)/det,v=cross(b,c,x,y)/det;
          return [x,y,u*c[2]+v*a[2]+(1-u-v)*b[2]+lift];
        }),color);
      }
    }
    const white=gene("white",.65), shift=.065*Math.sin(gene("patternSeed",.35)*5.4)-.018;
    // Continuous asymmetric ink/ivory division, from the widow's peak to the throat.
    const blaze=[[shift+.008,.054+white*.25],[shift+.066,.068+white*.12],[.108,.001],
      [.16,-.061],[.237+.095*white,-.108],[.35+.16*white,-.111],
      [.34+.14*white,-.192],[.47,-.297],[.25,-.43],[-.05,-.49],
      [-.39,-.378],[-.43-.085*white,-.227],[-.32-.102*white,-.161],
      [-.18,-.096],[-.081+shift,-.007]];
    const heart=[.01,-.228];
    for(let k=0;k<blaze.length;k++) patch(paint,[heart,blaze[k],blaze[(k+1)%blaze.length]],cream);
    // Discrete subtle warm marks on the dark temple keep dark coats readable.
    for(const s of [-1,1]){
      const map=pts=>pts.map(([x,y])=>[x*s,y]);
      patch(paint,map([[.302,.143],[.406,.173],[.475,.155],[.39,.146]]),mix(dark,warm,.46));
      const tint=mix(dark,ink,.14+.35*gene("bands",.4));
      patch(paint,map([[.41,-.048],[.542,-.055],[.632,-.014],[.546,-.026]]),tint);
    }
    draw(paint,'Asymmetric blaze',true);

    // A five-sided cupped shell gives both pinnae a visible tragus, folded lip
    // and back plane; the two different listening angles affect their silhouettes.
    for(const sign of [-1,1]) {
      const outward=sign===1?.105:0, height=.470+.176*gene("ears",.55);
      const anchor=[sign*.360,.25,-.082];
      const p=(x,y,z)=>[anchor[0]+sign*(x*1.17+.095*y+outward*y),anchor[1]+height*y-(sign===1?.071*y:0),anchor[2]+z];
      const rim=[p(-.230,.06,.128),p(-.153,.44,.114),p(.113,1,-.035),
        p(.152,.982,-.048),p(.245,.45,.011),p(.231,.071,.065),p(.006,-.076,.132)];
      const inner=[p(-.149,.17,.144),p(-.105,.43,.126),p(.097,.845,.003),
        p(.114,.836,-.004),p(.184,.42,.071),p(.153,.17,.112),p(.026,.112,.15)];
      const rear=p(.03,.36,-.172), bowl=p(.031,.385,.007), rose=make('#c98881'), deep=make('#80585e');
      for(let k=0;k<rim.length;k++){
        const j=(k+1)%rim.length;
        emit(ears,[rim[k],rim[j],inner[j],inner[k]],k===1||k===2?mix(warm,rose,.13):warm);
        emit(ears,[inner[k],inner[j],bowl],k<2?deep:k===5?mix(rose,cream,.30):rose);
        emit(ears,[rim[j],rim[k],rear],dark,'z',-1);
      }
      emit(ears,[p(-.101,.195,.142),p(-.005,.27,.129),p(.043,.18,.146)],mix(cream,rose,.30));
    }
    draw(ears,'Listening ears');

    const gaze=make('#152a30'), jade=make('#75ada1'), honey=make('#ccbf79'), eyeRim=make('#253635');
    const jadeShadow=make('#375e57'), gleamColor=make('#fff6da'), pinColor=make('#e1efc8');
    const openness=gene("eyes",.54);
    function eyeAt(x,y){return [x,y,depthAt(x,y)+.012];}
    function dab(batch,poly,col,offset=.0) {
      const pts=poly.map(([x,y])=>{const p=eyeAt(x,y);p[2]+=offset;return p;});
      emit(batch,pts,col);
    }
    for(const sign of [-1,1]) {
      const cx=sign*.268, cy=.014, rx=.144+.045*openness;
      const ry=(.067+.033*openness)*(kitten?1.19:1);
      function orbit(dx,dy){return [cx+sign*rx*dx,cy+ry*dy+.012*dx];}
      const outline=[[-1,-.12],[-.75,.62],[-.28,1],[.15,1.04],[.63,.74],[1,.23],
        [.79,-.37],[.33,-.85],[-.18,-.97],[-.64,-.72]];
      // Map the lens over the actual cheek depth at a fine enough resolution to
      // avoid a planar sticker detaching when seen in profile.
      const irisAt=(u,v)=>{
        const pupil=((u-.037*sign)/.34)**2+((v-.02)/.83)**2;
        const outer=Math.sqrt(u*u*.74+v*v*.68);
        const iris=mix(jade,honey,.69*clamp((1-v)/2)**1.4);
        iris.lerp(jadeShadow,.32*clamp(v)+.29*clamp((outer-.70)*3));
        iris.lerp(gaze,clamp((1.035-pupil)*13));
        const gleam=((u+.145*sign)/.146)**2+((v-.42)/.188)**2;
        iris.lerp(gleamColor,clamp((1.13-gleam)*4));
        const pin=((u-.43*sign)/.055)**2+((v+.38)/.064)**2;
        return iris.lerp(pinColor,.76*clamp((1.15-pin)*3));
      };
      const lens=[], lensRGB=[], indices=[];
      const top=[-.12,.62,.91,1.03,1,.80,.53,.23], bottom=[-.12,-.55,-.82,-.96,-.90,-.63,-.30,.23];
      const interp=(a,t)=>{const k=Math.min(6,Math.floor(t*7));return lerp(a[k],a[k+1],t*7-k);};
      for(let j=0;j<=22;j++) for(let i=0;i<=44;i++) {
        const t=i/44, s=j/22, u=2*t-1, v=lerp(interp(bottom,t),interp(top,t),s);
        const [x,y]=orbit(u,v);
        const z=depthAt(x,y)+.013+.017*Math.sin(t*Math.PI)*Math.sin(s*Math.PI);
        lens.push(x,y,z);
        const col=irisAt(u,v), edge=clamp((.081-Math.min(t,1-t,s,1-s))/.066);
        col.lerp(eyeRim,.91*edge);lensRGB.push(col.r,col.g,col.b);
        if(i&&j){const k=j*45+i;
          if(sign>0)indices.push(k-46,k-45,k,k-46,k,k-1);
          else indices.push(k-46,k,k-45,k-46,k-1,k);}
      }
      const g=new T.BufferGeometry();
      g.setAttribute('position',new T.Float32BufferAttribute(lens,3));
      g.setAttribute('color',new T.Float32BufferAttribute(lensRGB,3));
      g.setIndex(indices);g.computeVertexNormals();
      const eyeMesh=new T.Mesh(g,new T.MeshStandardMaterial({vertexColors:true,roughness:.48}));
      eyeMesh.name='Jade eye'; head.add(eyeMesh);
      const upper=outline.slice(0,6);
      const edge=upper.map(([x,y])=>orbit(x,y+.055)).concat(upper.slice().reverse().map(([x,y])=>orbit(x,y-.068)));
      for(let k=0;k<5;k++)dab(eyes,[edge[k],edge[k+1],edge[10-k],edge[11-k]],ink,.008);
    }
    draw(eyes,'Upper lashes',true);

    const brick=make('#be807a'), lipColor=make('#72675e');
    const noseY=-.131;
    const at=(x,y,d)=>[x,y,depthAt(x,y)+d];
    const tip=at(-.004,noseY-.046,.037), mid=at(0,noseY+.012,.060);
    const l=at(-.088,noseY+.021,.015),r=at(.088,noseY+.021,.015);
    const bridge=at(0,noseY+.036,.008), lowL=at(-.068,noseY-.012,.010),lowR=at(.068,noseY-.012,.010);
    emit(nose,[l,bridge,mid],mix(brick,cream,.21));emit(nose,[bridge,r,mid],mix(brick,cream,.12));
    emit(nose,[l,mid,tip,lowL],brick); emit(nose,[mid,r,lowR,tip],mix(brick,ink,.21));
    const mouthEnd=-.223;
    function stroke(batch, points, width, color) {
      for(let k=0;k<points.length-1;k++) {
        const a=points[k],b=points[k+1], dx=b[0]-a[0],dy=b[1]-a[1], len=Math.hypot(dx,dy)||1;
        const w=width*(1-k/points.length*.68);
        const ox=-dy*w/len,oy=dx*w/len;
        emit(batch,[[a[0]+ox,a[1]+oy,a[2]],[a[0]-ox,a[1]-oy,a[2]],
          [b[0]-ox*.8,b[1]-oy*.8,b[2]],[b[0]+ox*.8,b[1]+oy*.8,b[2]]],color);
      }
    }
    const mouth=(x,y)=>at(x,y,.005);
    stroke(nose,[tip,mouth(0,mouthEnd)],.006,lipColor);
    for(const sign of [-1,1]) {
      stroke(nose,[mouth(0,mouthEnd),mouth(sign*.047,-.244),mouth(sign*.098,-.242),mouth(sign*.158,-.218)],.008,lipColor);
      const col=mix(cream,make('#969589'),.45);
      for(let i=0;i<3;i++){
        const x=sign*(.196+i*.019), y=-.161-i*.028,z=depthAt(x,y)+.009;
        dab(nose,[[x-.008,y],[x,y+.007],[x+.008,y],[x,y-.005]],mix(brick,ink,.30));
        const reach=.77+i*.026;
        stroke(whiskers,[[x,y,z],[sign*.396,y+.042-i*.019,z+.043],
          [sign*.585,y+.064-i*.039,z+.036],[sign*reach,y+.063-i*.057,z+.009]],.0047,col);
      }
    }
    draw(nose,'Nose and muzzle');
    draw(whiskers,'Whiskers',true);
    return head;
  }
  window.PaperHead={build};
})();
