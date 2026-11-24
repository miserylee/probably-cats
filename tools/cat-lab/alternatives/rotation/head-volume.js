/* Adult skull profiles in the model's rest space. Frontal landmarks retain
   their screen coordinates while the nose bridge, pads and occiput acquire
   their own depths around the head. */
(() => {
  "use strict";
  const clamp = t => Math.max(0, Math.min(1, t));
  const smooth = (lo,hi,v) => {
    const t=clamp((v-lo)/(hi-lo));
    return t*t*(3-2*t);
  };
  const gaussian = (x,r) => Math.exp(-x*x/(r*r));

  // The nasal stop sits between the orbits. The back of the braincase is
  // fullest opposite them, well above the angle of the mandible.
  const profile = [
    [1.99,.53,-.18],[2.07,.64,-.211],[2.12,.658,-.246],
    [2.15,.674,-.278],[2.19,.712,-.311],[2.218,.749,-.327],
    [2.245,.763,-.340],[2.273,.747,-.351],[2.32,.707,-.365],
    [2.37,.661,-.375],[2.43,.612,-.379],[2.485,.585,-.376],
    [2.548,.576,-.359],[2.596,.551,-.338],[2.646,.511,-.306],
    [2.705,.430,-.244],[2.756,.325,-.153],[2.792,.238,-.078]
  ];

  function ridgeAt(y,k) {
    const rows=profile;
    let i=0;
    while(i+2<rows.length && rows[i+1][0]<y) i++;
    const a=rows[i],b=rows[i+1],length=b[0]-a[0];
    const t=clamp((y-a[0])/length),delta=b[k]-a[k];
    const before=rows[Math.max(0,i-1)],after=rows[Math.min(rows.length-1,i+2)];
    const ma=(b[k]-before[k])*length/(b[0]-before[0]);
    const mb=(after[k]-a[k])*length/(after[0]-a[0]);
    return a[k]+t*(ma+t*(3*delta-2*ma-mb+t*(ma+mb-2*delta)));
  }

  function sectionsOf(position,count) {
    const bottom=2.005,step=.017,rows=Array.from({length:46},()=>[0,0,-Infinity,Infinity]);
    for(let i=0;i<count;i+=3)for(let j=0;j<3;j++) {
      const a=i+j,b=i+(j+1)%3,y0=position.getY(a),y1=position.getY(b);
      if(Math.abs(y1-y0)<1e-6)continue;
      const first=Math.max(0,Math.ceil((Math.min(y0,y1)-bottom)/step));
      const end=Math.min(rows.length-1,Math.floor((Math.max(y0,y1)-bottom)/step));
      for(let k=first;k<=end;k++) {
        const t=(bottom+k*step-y0)/(y1-y0);
        const x=position.getX(a)+(position.getX(b)-position.getX(a))*t-.006;
        const z=position.getZ(a)+(position.getZ(b)-position.getZ(a))*t;
        const side=x<0?0:1;
        const row=rows[k];
        row[side]=Math.max(row[side],Math.abs(x));
        row[2]=Math.max(row[2],z);row[3]=Math.min(row[3],z);
      }
    }
    const first=rows.findIndex(row=>Number.isFinite(row[2]));
    for(let i=0;i<rows.length;i++)if(!Number.isFinite(rows[i][2]))rows[i]=rows[Math.max(first,i-1)];
    return (x,y)=>{
      const t=Math.max(0,Math.min(rows.length-1.001,(y-bottom)/step));
      const k=Math.floor(t),r=t-k,side=x<.006?0:1;
      function interpolate(j) {
        const lo=rows[Math.max(0,k-1)][j]+2*rows[k][j]+rows[k+1][j];
        const hi=rows[k][j]+2*rows[k+1][j]+rows[Math.min(rows.length-1,k+2)][j];
        return (lo+(hi-lo)*r)/4;
      }
      return {radius:Math.max(.05,interpolate(side)),front:interpolate(2),rear:interpolate(3)};
    };
  }

  function sculpt(x,y,z,section,genes) {
    // The open jaw already matches the throat polygon edge for edge.
    const jaw=smooth(2.123,2.207,y);
    if(jaw===0)return z;
    const {radius,front,rear}=section;
    const temple=.018+.10*smooth(2.35,2.72,y);
    const breadth=Math.abs(x-.006);
    const across=Math.min(1,breadth/radius),ellipse=Math.max(0,1-across*across);
    if(z<temple) {
      const depth=clamp((temple-z)/Math.max(.035,temple-rear));
      const back=ridgeAt(y,2);
      const corrected=z+depth*(back-rear);
      const vault=temple+(back-temple)*Math.pow(ellipse,.52);
      const blend=.90*smooth(.012,.13,temple-z)*(1-smooth(2.70,2.80,y));
      return z+jaw*(corrected+blend*(vault-corrected)-z);
    }
    const depth=clamp((z-temple)/Math.max(.05,front-temple));
    const stop=gaussian(breadth,.23);
    // The ridge descends to the nose between two eye sockets. The brow
    // and paired whisker pads stand in front of the planes around them.
    const cx=x<0?.252:.236,cy=x<0?2.380:2.431;
    const eyeHeight=y-cy-(x<0?.10:.20)*(breadth-cx);
    const socketRadius=Math.hypot((breadth-cx)/.168,eyeHeight/.098);
    const socket=gaussian(socketRadius,.86);
    const brow=gaussian(breadth-cx,.115)*gaussian(eyeHeight-.106,.045);
    const pad=gaussian(breadth-.140,.097)*gaussian(y-2.207,.069);
    const cheek=gaussian(breadth-.37,.105)*gaussian(y-2.302,.086);
    const ridge=ridgeAt(y,1);
    const midline=(ridge-front)*(.32+.68*stop);
    const surface=z+jaw*smooth(.04,.85,depth)*(midline+.016*brow+.043*pad+.024*cheek);
    // Wrap the whole orbital plane over one vault so its outer corner and
    // the temple behind it have support from the same cheekbone.
    const nose=.11*gaussian(y-2.245,.091);
    const muzzleLength=(genes.muzzle-.5)*.12*gaussian(y-2.247,.13);
    const dome=temple+(ridge-nose-temple)*Math.pow(ellipse,.40);
    const medial=gaussian(breadth-.114,.066)*gaussian(eyeHeight+.046,.045);
    const zygoma=gaussian(breadth-.35,.135)*gaussian(eyeHeight,.145);
    const target=dome+(nose+muzzleLength)*gaussian(breadth,.132)
      -.018*socket+.021*brow+.062*pad+.018*cheek-.020*medial+.034*zygoma;
    const wrap=jaw*(1-smooth(2.61,2.76,y))*smooth(.005,.21,z-temple);
    return surface+.95*wrap*(target-surface);
  }

  function apply(T,root,genes) {
    if(genes.age==="kitten")return;
    const skull=root.getObjectByName("Carved skull, muzzle and listening ears");
    if(!skull)return;
    const g=skull.geometry,position=g.getAttribute("position"),normal=g.getAttribute("normal");
    const count=skull.userData.skullVertexCount ?? g.userData.pinnae?.[0]?.start;
    if(!count)return;
    const sectionAt=sectionsOf(position,count);
    const whiskerStart=skull.userData.whiskerStart ?? position.count;
    // The carving has duplicated vertices at every painted triangle. Weld
    // their normals in the skull only; ears and whiskers retain their edges.
    const shared=new Map(),groups=[];
    for(let i=0;i<position.count;i++) {
      if(i>=count && i<whiskerStart)continue;
      const x=position.getX(i),y=position.getY(i),z=position.getZ(i);
      const key=x+"/"+y+"/"+z;
      let group=shared.get(key);
      if(!group) {
        group={z:sculpt(x,y,z,sectionAt(x,y),genes),normal:new T.Vector3(),neighbours:new Set()};
        shared.set(key,group);
      }
      position.setZ(i,group.z);
      if(i<count)groups.push(group);
    }
    const a=new T.Vector3(),b=new T.Vector3(),c=new T.Vector3(),flat=new T.Vector3();
    for(let i=0;i<count;i+=3) {
      a.fromBufferAttribute(position,i);
      b.fromBufferAttribute(position,i+1).sub(a);
      c.fromBufferAttribute(position,i+2).sub(a);
      flat.crossVectors(b,c);
      for(let j=0;j<3;j++) {
        const g=groups[i+j],next=groups[i+(j+1)%3];
        g.normal.add(flat);g.neighbours.add(next);next.neighbours.add(g);
      }
    }
    for(const group of shared.values())group.normal.normalize();
    // A painted horizontal section is sampled much more densely than the
    // space between sections. Diffuse its lighting across connected skin
    // so that disconnected fur tufts, ear walls and lip edges stay crisp.
    for(let pass=0;pass<2;pass++) {
      for(const g of shared.values()) {
        const n=g.normal;
        const sum=a.copy(n).multiplyScalar(2);
        for(const next of g.neighbours) {
          const agreement=Math.max(0,n.dot(next.normal));
          sum.addScaledVector(next.normal,agreement*agreement);
        }
        g.shade=sum.normalize().clone();
      }
      for(const g of shared.values())g.normal.copy(g.shade);
    }
    for(let i=0;i<count;i+=3) {
      a.fromBufferAttribute(position,i);
      b.fromBufferAttribute(position,i+1).sub(a);
      c.fromBufferAttribute(position,i+2).sub(a);
      flat.crossVectors(b,c).normalize();
      for(let j=i;j<i+3;j++) {
        const y=position.getY(j),z=position.getZ(j);
        const soft=genes.furLength==="hairless" ? .95 :
          .24+.57*(1-smooth(.04,.47,z));
        const n=b.copy(flat).lerp(groups[j].normal,soft).normalize();
        a.fromBufferAttribute(normal,j).lerp(n,smooth(2.123,2.185,y)).normalize();
        normal.setXYZ(j,a.x,a.y,a.z);
      }
    }
    position.needsUpdate=true;normal.needsUpdate=true;
    g.computeBoundingSphere();
  }
  window.CatHeadVolume={apply};
})();
