// Exercise real animation frames and competing navigation, not keyframe declarations.
window.verifyPageMotion=async()=>{
 const passed=[],ok=(value,name)=>{if(!value)throw new Error(name);passed.push(name);},wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
 const content=document.querySelector('#content'),savedTransition=document.startViewTransition,savedAnimate=content.animate,savedRender=renderPage;
 let snapshots=0,animations=0,changes=[],sampling=true,sharedError=null;window.motionFrameSamples=[];
 const observer=new MutationObserver(()=>changes.push({phase:content.dataset.motionPhase,opacity:Number(getComputedStyle(content).opacity)}));
 const sample=()=>{if(!sampling)return;if(content.dataset.motionPhase)window.motionFrameSamples.push({page:state.page,phase:content.dataset.motionPhase,opacity:Number(getComputedStyle(content).opacity)});requestAnimationFrame(sample);};
 try{
  setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:true});await navigate('home');
  observer.observe(content,{childList:true});requestAnimationFrame(sample);
  document.startViewTransition=callback=>{snapshots++;return savedTransition.call(document,callback);};
  content.animate=function(frames,options){animations++;return savedAnimate.call(this,frames,options);};
  await navigate('jobs');await wait(250);await navigate('more');await wait(250);await navigate('interviews');
  ok(snapshots===0&&animations===6,'ordinary routes animate one live content layer without old-page snapshots');
  ok(changes.length>=3&&changes.every(change=>change.phase==='updating'&&change.opacity<.01),'all asynchronous page replacements occur while the live content layer is invisible');
  ok(window.motionFrameSamples.some(frame=>frame.opacity>.01&&frame.opacity<.99),'animation frames traverse real intermediate opacity values');
  observer.disconnect();content.animate=savedAnimate;
  let active=0,maximum=0,rendered=[];
  renderPage=async page=>{active++;maximum=Math.max(maximum,active);try{await wait(90);rendered.push(page);await savedRender(page);}finally{active--;}};
  const slow=navigate('jobs');await wait(120);
  const burst=['templates','home','more','interviews'].map(page=>navigate(page));await Promise.all([slow,...burst]);
  ok(maximum===1&&state.page==='interviews'&&rendered.join(',')==='jobs,interviews','rapid taps serialize rendering and discard superseded pending pages');
  ok(content.querySelector('h1')?.textContent==='面试题背诵'&&!content.dataset.motionPhase&&content.getAnimations().length===0,'the last selected page owns the content with no leftover page animation');
  renderPage=savedRender;await navigate('home');await wait(250);
  let sharedReady;const sharedStarted=new Promise(resolve=>sharedReady=resolve);
  document.startViewTransition=callback=>{
   snapshots++;const transition=savedTransition.call(document,callback);
   transition.ready.then(async()=>{
    try{ok(getComputedStyle(document.documentElement).viewTransitionName==='none','card expansion does not capture a full-page root snapshot');await wait(100);}
    catch(error){sharedError=error;}finally{sharedReady();}
   },sharedReady);return transition;
  };
  const source=content.querySelector('.grid .card[data-page="favorites"]');
  const card=sharedMotion(source,()=>renderPage('favorites'),()=>content.querySelector('.card')||content.querySelector('h1'));
  await sharedStarted;const latest=navigate('jobs');await Promise.all([card,latest]);if(sharedError)throw sharedError;
  ok(snapshots===1&&state.page==='jobs'&&!document.querySelector('[style*="view-transition-name"]')&&!content.classList.contains('shared-transition'),'a bottom-tab request during card expansion completes without nested snapshots or stale card names');
  document.startViewTransition=undefined;content.animate=undefined;await navigate('more');
  ok(state.page==='more'&&!content.dataset.motionPhase&&!content.classList.contains('page-leave'),'older WebViews complete the sequential CSS fallback and restore interaction');
  content.animate=savedAnimate;setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:false});await navigate('home');
  ok(state.page==='home'&&content.getAnimations().length===0,'disabled motion switches pages without waiting for an animation');
  return passed;
 }finally{sampling=false;observer.disconnect();renderPage=savedRender;content.animate=savedAnimate;document.startViewTransition=savedTransition;setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:true});}
};
